import { DOCUMENT } from '@angular/common';
import { OverlayModule, type ConnectedPosition } from '@angular/cdk/overlay';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { afterNextRender, Component, computed, HostListener, inject, Injector, OnDestroy, OnInit, signal } from '@angular/core';
import { forkJoin, Subscription } from 'rxjs';

interface ReceiptItem {
  'item name': string;
  'quantity purchased': number;
  'date of purchase': string;
  'cost per item': number;
  aliasMatched: boolean;
  foodItemId: string | null;
}

interface ReceiptRow extends ReceiptItem {
  id: number;
  originalGeminiName: string;
  draftCanonicalName: string;
  isEditing: boolean;
  isSaving: boolean;
  editError: string;
  suggestions: FoodItemSearchResponse[];
  isSearching: boolean;
  activeSuggestionIndex: number;
  searchCompleted: boolean;
  searchError: string;
}

interface ReceiptParseResponse {
  items: ReceiptItem[];
  hash: string;
}

interface FoodItemAliasResponse {
  canonicalName: string;
  foodItemId: string;
}

interface FoodItemSearchResponse {
  canonicalName: string;
  foodItemId: string;
}

interface PurchaseSaveResponse {
  hash: string;
  itemsSaved: number;
  alreadySaved: boolean;
}

interface PantryItemSummary {
  foodItemId: string;
  itemName: string;
  totalQuantity: number;
  totalSpent: number;
  oldestPurchaseDate: string;
  newestPurchaseDate: string;
  tags: string[];
}

type PantrySortColumn = 'itemName' | 'totalQuantity' | 'totalSpent';

interface PantrySummaryResponse {
  items: PantryItemSummary[];
  grandTotal: number;
}

interface FoodTagOption {
  name: string;
  usageCount: number;
}

interface FoodItemTagsResponse {
  foodItemId: string;
  tags: string[];
  availableTags: FoodTagOption[];
}

interface ManualRow {
  id: number;
  draftName: string;
  foodItemName: string | null;
  foodItemId: string | null;
  isEditing: boolean;
  isSaving: boolean;
  editError: string;
  suggestions: FoodItemSearchResponse[];
  isSearching: boolean;
  activeSuggestionIndex: number;
  searchCompleted: boolean;
  searchError: string;
  quantity: string;
  purchaseDate: string;
  costPerItem: string;
}

type ManualField = 'quantity' | 'purchaseDate' | 'costPerItem';

const supportedImageTypes = new Set([
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/heic',
  'image/heif',
]);
const maxImageSize = 10 * 1024 * 1024;

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  imports: [OverlayModule],
})
export class App implements OnDestroy, OnInit {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private nextRowId = 0;
  private readonly searchTimers = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly searchRequests = new Map<number, Subscription>();
  private pantrySummaryRequest: Subscription | null = null;
  private fileDragDepth = 0;
  protected readonly activePage = signal<'manualEntry' | 'receiptUploads' | 'pantryItems'>('pantryItems');
  protected readonly manualRows = signal<ManualRow[]>([]);
  protected readonly isSavingManualPurchases = signal(false);
  protected readonly manualPurchaseStatus = signal('');
  protected readonly manualPurchaseError = signal('');
  protected readonly highlightAddManualRow = computed(() => {
    const rows = this.manualRows();
    return rows.length > 0 && rows.every(row => !!row.foodItemId && !row.isEditing);
  });
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly isDraggingFile = signal(false);
  protected readonly items = signal<ReceiptRow[]>([]);
  protected readonly errorMessage = signal('');
  protected readonly isLoading = signal(false);
  protected readonly receiptHash = signal<string | null>(null);
  protected readonly isSavingPurchases = signal(false);
  protected readonly purchasesSaved = signal(false);
  protected readonly duplicateReceipt = signal(false);
  protected readonly savedPurchaseCount = signal<number | null>(null);
  protected readonly purchaseError = signal('');
  protected readonly pantrySummary = signal<PantrySummaryResponse | null>(null);
  protected readonly isLoadingPantry = signal(false);
  protected readonly pantryError = signal('');
  protected readonly showPantryFilters = signal(false);
  protected readonly pantryNameFilter = signal('');
  protected readonly pantryFromDate = signal('');
  protected readonly pantryToDate = signal('');
  protected readonly selectedPantryTags = signal<string[]>([]);
  protected readonly isPantryTagFilterOpen = signal(false);
  protected readonly availableTags = signal<FoodTagOption[]>([]);
  protected readonly orderedPantryTags = computed(() => {
    const selectedTags = new Set(this.selectedPantryTags().map(tag => tag.toLocaleLowerCase()));
    const tags = this.availableTags();
    return [
      ...tags.filter(tag => selectedTags.has(tag.name.toLocaleLowerCase())),
      ...tags.filter(tag => !selectedTags.has(tag.name.toLocaleLowerCase())),
    ];
  });
  protected readonly editingFoodItemId = signal<string | null>(null);
  protected readonly draftTags = signal<string[]>([]);
  protected readonly tagSearchQuery = signal('');
  protected readonly tagSuggestionsOpen = signal(false);
  protected readonly activeTagSuggestionIndex = signal(0);
  protected readonly isSavingTags = signal(false);
  protected readonly tagEditorError = signal('');
  protected readonly pantryTagFilterPositions: ConnectedPosition[] = [
    { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top' },
    { originX: 'end', originY: 'top', overlayX: 'end', overlayY: 'bottom' },
  ];
  protected readonly pantrySort = signal<{ column: PantrySortColumn; direction: 'asc' | 'desc' } | null>(null);
  protected readonly filteredPantryItems = computed(() => {
    const query = this.pantryNameFilter().trim().toLocaleLowerCase();
    const selectedTags = new Set(this.selectedPantryTags().map(tag => tag.toLocaleLowerCase()));
    const sort = this.pantrySort();

    const items = (this.pantrySummary()?.items ?? []).filter(item => {
      const matchesName = !query || item.itemName.toLocaleLowerCase().includes(query);
      const matchesTags = !selectedTags.size
        || item.tags.some(tag => selectedTags.has(tag.toLocaleLowerCase()));
      return matchesName && matchesTags;
    });

    if (!sort) {
      return items;
    }

    const factor = sort.direction === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => factor * (sort.column === 'itemName'
      ? a.itemName.localeCompare(b.itemName, undefined, { sensitivity: 'base' })
      : a[sort.column] - b[sort.column]));
  });
  protected readonly filteredPantryGrandTotal = computed(() =>
    this.filteredPantryItems().reduce((total, item) => total + item.totalSpent, 0));
  protected readonly filteredPantryPurchaseSpanDays = computed(() => {
    const items = this.filteredPantryItems();
    const fromDate = this.pantryFromDate();
    let oldestDate: string | null = fromDate || null;
    let newestDate: string | null = null;
    for (const item of items) {
      if (!fromDate && (oldestDate === null || item.oldestPurchaseDate < oldestDate)) {
        oldestDate = item.oldestPurchaseDate;
      }
      if (newestDate === null || item.newestPurchaseDate > newestDate) {
        newestDate = item.newestPurchaseDate;
      }
    }

    return oldestDate && newestDate
      ? (Date.parse(newestDate) - Date.parse(oldestDate)) / 86400000
      : null;
  });
  protected readonly filteredPantryDailySpend = computed(() => {
    const days = this.filteredPantryPurchaseSpanDays();
    return days !== null && days > 0 ? this.filteredPantryGrandTotal() / days : 0;
  });
  protected readonly filteredPantryMonthlySpend = computed(() => {
    const dailySpend = this.filteredPantryDailySpend();
    const today = new Date();
    const daysInCurrentMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate();
    return dailySpend * daysInCurrentMonth;
  });

  ngOnInit(): void {
    this.manualRows.set([this.createManualRow()]);
    const today = new Date();
    const yearMonth = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`;
    const lastDayOfMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate();
    this.showPantryFilters.set(true);
    this.pantryFromDate.set(`${yearMonth}-01`);
    this.pantryToDate.set(`${yearMonth}-${String(lastDayOfMonth).padStart(2, '0')}`);
    this.loadPantrySummary();
  }

  ngOnDestroy(): void {
    this.clearFoodSearches();
    this.pantrySummaryRequest?.unsubscribe();
  }

  protected showPage(page: 'manualEntry' | 'receiptUploads' | 'pantryItems'): void {
    this.activePage.set(page);
    if (page === 'pantryItems') {
      this.loadPantrySummary();
    }
  }

  protected formatQuantity(quantity: number): string {
    return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(quantity);
  }

  protected updatePantryNameFilter(event: Event): void {
    if (this.editingFoodItemId()) {
      return;
    }

    this.pantryNameFilter.set((event.target as HTMLInputElement).value);
  }

  protected updatePantryDateRange(boundary: 'from' | 'to', event: Event): void {
    if (this.editingFoodItemId()) {
      return;
    }

    const input = event.target as HTMLInputElement;
    const date = input.value;
    const otherDate = boundary === 'from' ? this.pantryToDate() : this.pantryFromDate();
    if (date && otherDate && (boundary === 'from' ? date > otherDate : date < otherDate)) {
      input.value = boundary === 'from' ? this.pantryFromDate() : this.pantryToDate();
      return;
    }

    if (boundary === 'from') {
      this.pantryFromDate.set(date);
    } else {
      this.pantryToDate.set(date);
    }
    this.loadPantrySummary();
  }

  protected togglePantryFilters(): void {
    this.showPantryFilters.update(showFilters => !showFilters);
    if (!this.showPantryFilters()) {
      this.isPantryTagFilterOpen.set(false);
      this.pantryNameFilter.set('');
      this.selectedPantryTags.set([]);

      const hadDateFilter = !!this.pantryFromDate() || !!this.pantryToDate();
      this.pantryFromDate.set('');
      this.pantryToDate.set('');
      if (hadDateFilter) {
        this.loadPantrySummary();
      }
    }
  }

  protected togglePantryTagFilter(): void {
    if (this.editingFoodItemId() || !this.availableTags().length) {
      return;
    }

    this.isPantryTagFilterOpen.update(isOpen => !isOpen);
  }

  protected closePantryTagFilter(trigger?: HTMLButtonElement): void {
    this.isPantryTagFilterOpen.set(false);
    trigger?.focus();
  }

  protected togglePantryTagSelection(tagName: string): void {
    if (this.editingFoodItemId()) {
      return;
    }

    this.selectedPantryTags.update(selectedTags => selectedTags.some(tag =>
      tag.toLocaleLowerCase() === tagName.toLocaleLowerCase())
      ? selectedTags.filter(tag => tag.toLocaleLowerCase() !== tagName.toLocaleLowerCase())
      : [...selectedTags, tagName]);
  }

  protected unselectAllPantryTags(): void {
    if (this.editingFoodItemId()) {
      return;
    }

    this.selectedPantryTags.set([]);
  }

  private updateAvailableTags(tags: FoodTagOption[]): void {
    this.availableTags.set(tags);
    const availableTagNames = new Set(tags.map(tag => tag.name.toLocaleLowerCase()));
    this.selectedPantryTags.update(selectedTags => selectedTags.filter(tag =>
      availableTagNames.has(tag.toLocaleLowerCase())));
  }

  protected startEditingTags(item: PantryItemSummary): void {
    this.isPantryTagFilterOpen.set(false);
    this.editingFoodItemId.set(item.foodItemId);
    this.draftTags.set([...item.tags]);
    this.resetTagSuggestions();
    this.tagEditorError.set('');

    afterNextRender(() => {
      this.document.getElementById(this.tagNameInputId(item.foodItemId))?.focus();
    }, { injector: this.injector });
  }

  protected tagNameInputId(foodItemId: string): string {
    return `tag-name-input-${foodItemId}`;
  }

  protected matchingTagSuggestions(): string[] {
    const query = this.tagSearchQuery().trim().toLocaleLowerCase();
    if (!query) {
      return [];
    }

    const assignedTags = new Set(this.draftTags().map(tag => tag.toLocaleLowerCase()));
    return this.availableTags()
      .filter(tag => tag.name.toLocaleLowerCase().startsWith(query)
        && !assignedTags.has(tag.name.toLocaleLowerCase()))
      .map(tag => tag.name);
  }

  protected updateTagSearch(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.tagSearchQuery.set(input.value);
    this.activeTagSuggestionIndex.set(0);
    this.tagSuggestionsOpen.set(true);
  }

  protected handleTagSuggestionKeydown(
    foodItemId: string,
    input: HTMLInputElement,
    event: KeyboardEvent,
  ): void {
    const suggestions = this.matchingTagSuggestions();
    if (suggestions.length && event.key === 'ArrowDown') {
      event.preventDefault();
      this.tagSuggestionsOpen.set(true);
      this.activeTagSuggestionIndex.update(index => (index + 1) % suggestions.length);
      return;
    }

    if (suggestions.length && event.key === 'ArrowUp') {
      event.preventDefault();
      this.tagSuggestionsOpen.set(true);
      this.activeTagSuggestionIndex.update(index => (index - 1 + suggestions.length) % suggestions.length);
      return;
    }

    if (event.key === 'Escape' && this.tagSuggestionsOpen()) {
      event.preventDefault();
      this.tagSuggestionsOpen.set(false);
      return;
    }

    if (event.key === 'Enter' && this.tagSuggestionsOpen() && suggestions.length) {
      event.preventDefault();
      this.selectTagSuggestion(input, suggestions[this.activeTagSuggestionIndex()]);
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      if (input.value.trim()) {
        this.addDraftTag(input);
      } else {
        this.saveFoodItemTags(foodItemId, input);
      }
    }
  }

  protected selectTagSuggestion(input: HTMLInputElement, tagName: string): void {
    input.value = tagName;
    this.addDraftTag(input);
    input.focus();
  }

  protected closeTagSuggestions(): void {
    this.tagSuggestionsOpen.set(false);
  }

  protected addDraftTag(input: HTMLInputElement, event?: Event): void {
    event?.preventDefault();
    const tagName = input.value.trim();
    if (!tagName) {
      this.resetTagSuggestions();
      return;
    }

    if (!this.draftTags().some(tag => tag.toLowerCase() === tagName.toLowerCase())) {
      this.draftTags.update(tags => [...tags, tagName]);
    }
    input.value = '';
    this.resetTagSuggestions();
  }

  protected removeDraftTag(tagName: string): void {
    this.draftTags.update(tags => tags.filter(tag => tag.toLowerCase() !== tagName.toLowerCase()));
  }

  protected cancelTagEditing(): void {
    this.editingFoodItemId.set(null);
    this.draftTags.set([]);
    this.resetTagSuggestions();
    this.tagEditorError.set('');
  }

  private resetTagSuggestions(): void {
    this.tagSearchQuery.set('');
    this.tagSuggestionsOpen.set(false);
    this.activeTagSuggestionIndex.set(0);
  }

  protected saveFoodItemTags(foodItemId: string, input: HTMLInputElement): void {
    if (this.isSavingTags() || this.editingFoodItemId() !== foodItemId) {
      return;
    }

    this.addDraftTag(input);
    this.isSavingTags.set(true);
    this.tagEditorError.set('');
    this.http.put<FoodItemTagsResponse>(
      `/api/food-items/${encodeURIComponent(foodItemId)}/tags`,
      { tags: this.draftTags() },
    ).subscribe({
      next: (response) => {
        this.pantrySummary.update(summary => summary
          ? {
              ...summary,
              items: summary.items.map(item => item.foodItemId === foodItemId
                ? { ...item, tags: response.tags }
                : item),
            }
          : summary);
        this.updateAvailableTags(response.availableTags);
        this.isSavingTags.set(false);
        this.cancelTagEditing();
      },
      error: (response: HttpErrorResponse) => {
        this.tagEditorError.set(response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'Tags could not be saved. Check the backend connection and try again.');
        this.isSavingTags.set(false);
      },
    });
  }

  private loadPantrySummary(): void {
    this.pantrySummaryRequest?.unsubscribe();
    this.isLoadingPantry.set(true);
    this.pantryError.set('');
    let params = new HttpParams();
    if (this.pantryFromDate()) {
      params = params.set('fromDate', this.pantryFromDate());
    }
    if (this.pantryToDate()) {
      params = params.set('toDate', this.pantryToDate());
    }

    this.pantrySummaryRequest = forkJoin({
      summary: this.http.get<PantrySummaryResponse>('/api/purchases/summary', { params }),
      tags: this.http.get<FoodTagOption[]>('/api/tags'),
    }).subscribe({
      next: ({ summary, tags }) => {
        this.pantrySummary.set(summary);
        this.updateAvailableTags(tags);
        this.isLoadingPantry.set(false);
      },
      error: (response: HttpErrorResponse) => {
        const detail = response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'Pantry items could not be loaded. Check that the backend is running and configured.';
        this.pantryError.set(detail);
        this.isLoadingPantry.set(false);
      },
    });
  }

  protected sortPantryBy(column: PantrySortColumn): void {
    const current = this.pantrySort();
    if (current?.column === column) {
      this.pantrySort.set({ column, direction: current.direction === 'asc' ? 'desc' : 'asc' });
    } else {
      this.pantrySort.set({ column, direction: column === 'itemName' ? 'asc' : 'desc' });
    }
  }

  protected pantryAriaSort(column: PantrySortColumn): 'ascending' | 'descending' | 'none' {
    const sort = this.pantrySort();
    if (sort?.column !== column) {
      return 'none';
    }
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected addManualRow(): void {
    const row = this.createManualRow();
    this.manualRows.update(rows => [...rows, row]);

    afterNextRender(() => {
      (this.document.getElementById(this.foodNameInputId(row.id)) as HTMLInputElement | null)?.focus();
    }, { injector: this.injector });
  }

  protected removeManualRow(rowId: number): void {
    this.cancelFoodSearch(rowId);
    const remaining = this.manualRows().filter(row => row.id !== rowId);
    this.manualRows.set(remaining.length ? remaining : [this.createManualRow()]);
  }

  protected updateManualField(rowId: number, field: ManualField, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.updateManualRow(rowId, row => ({ ...row, [field]: value }));
  }

  protected startManualEditing(rowId: number): void {
    this.updateManualRow(rowId, row => ({ ...row, isEditing: true, draftName: row.foodItemName ?? row.draftName, editError: '' }));

    afterNextRender(() => {
      const input = this.document.getElementById(this.foodNameInputId(rowId)) as HTMLInputElement | null;
      input?.focus();
      input?.select();
    }, { injector: this.injector });
  }

  protected cancelManualEdit(rowId: number): void {
    this.cancelFoodSearch(rowId);
    this.updateManualRow(rowId, row => ({
      ...row,
      isEditing: false,
      draftName: row.foodItemName ?? '',
      editError: '',
      suggestions: [],
      isSearching: false,
      activeSuggestionIndex: 0,
      searchCompleted: false,
      searchError: '',
    }));
  }

  protected updateManualDraft(rowId: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    this.cancelFoodSearch(rowId);
    this.updateManualRow(rowId, row => ({
      ...row,
      draftName: input.value,
      editError: '',
      suggestions: [],
      isSearching: false,
      activeSuggestionIndex: 0,
      searchCompleted: false,
      searchError: '',
    }));

    const query = input.value.trim();
    if (!query) {
      return;
    }

    const timer = setTimeout(() => {
      this.searchTimers.delete(rowId);
      this.updateManualRow(rowId, row => ({ ...row, isSearching: true }));

      const request = this.http.get<FoodItemSearchResponse[]>(
        '/api/food-items/search',
        { params: { q: query } },
      ).subscribe({
        next: (suggestions) => {
          this.searchRequests.delete(rowId);
          this.updateManualRow(rowId, row => row.isEditing && row.draftName.trim() === query
            ? { ...row, suggestions, isSearching: false, activeSuggestionIndex: 0, searchCompleted: true, searchError: '' }
            : row);
        },
        error: () => {
          this.searchRequests.delete(rowId);
          this.updateManualRow(rowId, row => row.draftName.trim() === query
            ? { ...row, suggestions: [], isSearching: false, searchCompleted: false, searchError: 'Food suggestions could not be loaded.' }
            : row);
        },
      });
      this.searchRequests.set(rowId, request);
    }, 500);
    this.searchTimers.set(rowId, timer);
  }

  protected handleManualNameKeydown(rowId: number, event: KeyboardEvent): void {
    const row = this.manualRows().find(item => item.id === rowId);
    if (!row) {
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      if (row.suggestions.length) {
        this.selectManualSuggestion(rowId, row.suggestions[row.activeSuggestionIndex]);
      } else if (!row.isSearching && !this.searchTimers.has(rowId) && !this.searchRequests.has(rowId) && this.canConfirmManualName(row)) {
        this.confirmManualName(rowId);
      }
      return;
    }

    if (!row.suggestions.length) {
      if (event.key === 'Escape') {
        this.cancelFoodSearch(rowId);
        this.updateManualRow(rowId, item => ({ ...item, searchCompleted: false }));
      }
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const direction = event.key === 'ArrowDown' ? 1 : -1;
      const activeSuggestionIndex = (row.activeSuggestionIndex + direction + row.suggestions.length) % row.suggestions.length;
      this.updateManualRow(rowId, item => ({ ...item, activeSuggestionIndex }));
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.updateManualRow(rowId, item => ({ ...item, suggestions: [], activeSuggestionIndex: 0, searchCompleted: false }));
    }
  }

  protected selectManualSuggestion(rowId: number, suggestion: FoodItemSearchResponse): void {
    this.cancelFoodSearch(rowId);
    this.updateManualRow(rowId, row => ({
      ...row,
      draftName: suggestion.canonicalName,
      suggestions: [],
      isSearching: false,
      activeSuggestionIndex: 0,
      searchCompleted: false,
      searchError: '',
    }));
  }

  protected canConfirmManualName(row: ManualRow): boolean {
    const name = row.draftName.trim();
    return !row.isSaving && name.length > 0 && name.length <= 255;
  }

  protected confirmManualName(rowId: number): void {
    const row = this.manualRows().find(item => item.id === rowId);
    if (!row || !this.canConfirmManualName(row)) {
      return;
    }

    this.cancelFoodSearch(rowId);
    this.updateManualRow(rowId, item => ({ ...item, isSaving: true, editError: '', suggestions: [], isSearching: false, searchCompleted: false, searchError: '' }));

    this.http.post<FoodItemSearchResponse>('/api/food-items', {
      name: row.draftName.trim(),
    }).subscribe({
      next: (response) => {
        this.updateManualRow(rowId, item => ({
          ...item,
          foodItemId: response.foodItemId,
          foodItemName: response.canonicalName,
          draftName: response.canonicalName,
          isEditing: false,
          isSaving: false,
        }));
        afterNextRender(() => {
          (this.document.getElementById('manual-add-row') as HTMLButtonElement | null)?.focus();
        }, { injector: this.injector });
      },
      error: (response: HttpErrorResponse) => {
        const detail = response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'The food item could not be saved. Try again.';
        this.updateManualRow(rowId, item => ({ ...item, isSaving: false, editError: detail }));
      },
    });
  }

  protected canSaveManualPurchases(): boolean {
    const rows = this.manualRows().filter(row => !this.isBlankManualRow(row));
    return rows.length > 0 &&
      rows.every(row => this.isValidManualRow(row)) &&
      !this.isSavingManualPurchases();
  }

  protected saveManualPurchases(): void {
    if (!this.canSaveManualPurchases()) {
      return;
    }

    const lines = this.manualRows()
      .filter(row => !this.isBlankManualRow(row))
      .map(row => ({
        foodItemId: row.foodItemId!,
        name: row.foodItemName!,
        dateOfPurchase: row.purchaseDate,
        quantityPurchased: Number(row.quantity),
        costPerItem: Number(row.costPerItem),
      }))
      .sort((a, b) => this.manualLineKey(a) < this.manualLineKey(b) ? -1 : this.manualLineKey(a) > this.manualLineKey(b) ? 1 : 0);

    this.isSavingManualPurchases.set(true);
    this.manualPurchaseStatus.set('');
    this.manualPurchaseError.set('');

    const serialized = JSON.stringify(lines.map(line => [line.name, line.dateOfPurchase, line.quantityPurchased, line.costPerItem]));
    this.computeSha256Hex(serialized).then((hash) => {
      this.http.post<PurchaseSaveResponse>('/api/purchases', {
        hash,
        items: lines.map(line => ({
          foodItemId: line.foodItemId,
          dateOfPurchase: line.dateOfPurchase,
          quantityPurchased: line.quantityPurchased,
          costPerItem: line.costPerItem,
        })),
      }).subscribe({
        next: (response) => {
          this.clearFoodSearches();
          this.isSavingManualPurchases.set(false);
          this.manualRows.set([this.createManualRow()]);
          this.manualPurchaseStatus.set(response.alreadySaved
            ? 'These purchases were already saved. No new items were added.'
            : `Saved ${response.itemsSaved} ${response.itemsSaved === 1 ? 'item' : 'items'} to purchases.`);
        },
        error: (response: HttpErrorResponse) => {
          const detail = response.error && typeof response.error.detail === 'string'
            ? response.error.detail
            : 'Purchases could not be saved. Try again.';
          this.isSavingManualPurchases.set(false);
          this.manualPurchaseError.set(detail);
        },
      });
    });
  }

  protected selectImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.setSelectedImage(file, input);
  }

  @HostListener('document:dragenter', ['$event'])
  protected handleFileDragEnter(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    event.preventDefault();
    this.fileDragDepth += 1;
    this.isDraggingFile.set(true);
  }

  @HostListener('document:dragover', ['$event'])
  protected handleFileDragOver(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  @HostListener('document:dragleave', ['$event'])
  protected handleFileDragLeave(event: DragEvent): void {
    if (!this.hasFiles(event)) {
      return;
    }

    this.fileDragDepth = Math.max(0, this.fileDragDepth - 1);
    if (this.fileDragDepth === 0) {
      this.isDraggingFile.set(false);
    }
  }

  @HostListener('document:drop', ['$event'])
  protected handleFileDrop(event: DragEvent): void {
    const file = event.dataTransfer?.files[0];
    this.fileDragDepth = 0;
    this.isDraggingFile.set(false);
    if (!file) {
      return;
    }

    event.preventDefault();
    if (this.isLoading() || this.isSavingPurchases()) {
      return;
    }

    const input = this.document.getElementById('receipt-image') as HTMLInputElement | null;
    if (!input) {
      return;
    }

    const transfer = new DataTransfer();
    transfer.items.add(file);
    input.files = transfer.files;
    this.setSelectedImage(file, input);
    if (this.selectedFile()) {
      input.form?.requestSubmit();
    }
  }

  private hasFiles(event: DragEvent): boolean {
    return Array.from(event.dataTransfer?.types ?? []).includes('Files');
  }

  private setSelectedImage(file: File | null, input: HTMLInputElement): void {
    this.clearFoodSearches();
    this.items.set([]);
    this.errorMessage.set('');
    this.receiptHash.set(null);
    this.purchasesSaved.set(false);
    this.duplicateReceipt.set(false);
    this.savedPurchaseCount.set(null);
    this.purchaseError.set('');
    this.selectedFile.set(null);

    if (!file) {
      return;
    }

    if (!supportedImageTypes.has(file.type.toLowerCase())) {
      this.errorMessage.set('Choose a JPEG, PNG, WebP, HEIC, or HEIF image.');
      input.value = '';
      return;
    }

    if (file.size > maxImageSize) {
      this.errorMessage.set('Choose an image that is 10 MB or smaller.');
      input.value = '';
      return;
    }

    this.selectedFile.set(file);
  }

  protected submitReceipt(event: SubmitEvent): void {
    event.preventDefault();
    const file = this.selectedFile();
    if (!file || this.isLoading()) {
      return;
    }

    const formData = new FormData();
    formData.append('image', file, file.name);
    this.clearFoodSearches();
    this.items.set([]);
    this.errorMessage.set('');
    this.receiptHash.set(null);
    this.purchasesSaved.set(false);
    this.duplicateReceipt.set(false);
    this.savedPurchaseCount.set(null);
    this.purchaseError.set('');
    this.isLoading.set(true);

    this.http.post<ReceiptParseResponse>('/api/gemini/receipts/parse', formData).subscribe({
      next: (response) => {
        this.receiptHash.set(response.hash);
        this.items.set(response.items.map((item) => ({
          ...item,
          id: this.nextRowId++,
          originalGeminiName: item['item name'],
          draftCanonicalName: item['item name'],
          isEditing: false,
          isSaving: false,
          editError: '',
          suggestions: [],
          isSearching: false,
          activeSuggestionIndex: 0,
          searchCompleted: false,
          searchError: '',
        })));
        this.isLoading.set(false);
      },
      error: (response: HttpErrorResponse) => {
        const detail = response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'The receipt could not be parsed. Check that the backend is running and configured.';
        this.errorMessage.set(detail);
        this.isLoading.set(false);
      },
    });
  }

  protected startEditing(rowId: number): void {
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? { ...row, isEditing: true, draftCanonicalName: row['item name'], editError: '' }
      : row));

    afterNextRender(() => {
      const input = this.document.getElementById(this.foodNameInputId(rowId)) as HTMLInputElement | null;
      input?.focus();
      input?.select();
    }, { injector: this.injector });
  }

  protected foodNameInputId(rowId: number): string {
    return `food-name-input-${rowId}`;
  }

  protected updateDraft(rowId: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    this.cancelFoodSearch(rowId);
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? {
          ...row,
          draftCanonicalName: input.value,
          editError: '',
          suggestions: [],
          isSearching: false,
          activeSuggestionIndex: 0,
          searchCompleted: false,
          searchError: '',
        }
      : row));

    const query = input.value.trim();
    if (!query) {
      return;
    }

    const timer = setTimeout(() => {
      this.searchTimers.delete(rowId);
      this.items.update((rows) => rows.map((row) => row.id === rowId
        ? { ...row, isSearching: true }
        : row));

      const request = this.http.get<FoodItemSearchResponse[]>(
        '/api/food-items/search',
        { params: { q: query } },
      ).subscribe({
        next: (suggestions) => {
          this.searchRequests.delete(rowId);
          this.items.update((rows) => rows.map((row) => row.id === rowId && row.isEditing && row.draftCanonicalName.trim() === query
            ? { ...row, suggestions, isSearching: false, activeSuggestionIndex: 0, searchCompleted: true, searchError: '' }
            : row));
        },
        error: () => {
          this.searchRequests.delete(rowId);
          this.items.update((rows) => rows.map((row) => row.id === rowId && row.draftCanonicalName.trim() === query
            ? { ...row, suggestions: [], isSearching: false, searchCompleted: false, searchError: 'Food suggestions could not be loaded.' }
            : row));
        },
      });
      this.searchRequests.set(rowId, request);
    }, 500);
    this.searchTimers.set(rowId, timer);
  }

  protected handleSuggestionKeydown(rowId: number, event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      const row = this.items().find((item) => item.id === rowId);
      if (!row) {
        return;
      }

      if (row.suggestions.length) {
        this.selectSuggestion(rowId, row.suggestions[row.activeSuggestionIndex]);
      } else if (!row.isSearching && !this.searchTimers.has(rowId) && !this.searchRequests.has(rowId) && this.canSave(row)) {
        this.saveCorrection(rowId);
      }
      return;
    }

    const row = this.items().find((item) => item.id === rowId);
    if (!row?.suggestions.length) {
      if (event.key === 'Escape') {
        this.cancelFoodSearch(rowId);
        this.items.update((rows) => rows.map((item) => item.id === rowId
          ? { ...item, searchCompleted: false }
          : item));
      }
      return;
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const direction = event.key === 'ArrowDown' ? 1 : -1;
      const activeSuggestionIndex = (row.activeSuggestionIndex + direction + row.suggestions.length) % row.suggestions.length;
      this.items.update((rows) => rows.map((item) => item.id === rowId
        ? { ...item, activeSuggestionIndex }
        : item));
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.items.update((rows) => rows.map((item) => item.id === rowId
        ? { ...item, suggestions: [], activeSuggestionIndex: 0, searchCompleted: false }
        : item));
    }
  }

  protected selectSuggestion(rowId: number, suggestion: FoodItemSearchResponse): void {
    this.cancelFoodSearch(rowId);
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? {
          ...row,
          draftCanonicalName: suggestion.canonicalName,
          suggestions: [],
          isSearching: false,
          activeSuggestionIndex: 0,
          searchCompleted: false,
          searchError: '',
        }
      : row));
  }

  protected activeSuggestionId(row: ReceiptRow): string | null {
    return row.suggestions.length
      ? this.suggestionOptionId(row.id, row.activeSuggestionIndex)
      : null;
  }

  protected suggestionOptionId(rowId: number, index: number): string {
    return `food-suggestion-${rowId}-${index}`;
  }

  protected canSave(row: ReceiptRow): boolean {
    return !row.isSaving && row.draftCanonicalName.trim().length > 0 && row.draftCanonicalName.trim().length <= 255;
  }

  protected cancelEdit(rowId: number): void {
    this.cancelFoodSearch(rowId);
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? {
          ...row,
          isEditing: false,
          draftCanonicalName: row['item name'],
          editError: '',
          suggestions: [],
          isSearching: false,
          activeSuggestionIndex: 0,
          searchCompleted: false,
          searchError: '',
        }
      : row));
  }

  protected saveCorrection(rowId: number): void {
    const row = this.items().find((item) => item.id === rowId);
    if (!row || !this.canSave(row)) {
      return;
    }

    const canonicalName = row.draftCanonicalName.trim();
    this.cancelFoodSearch(rowId);
    this.items.update((rows) => rows.map((item) => item.id === rowId
      ? { ...item, isSaving: true, editError: '', suggestions: [], isSearching: false, searchCompleted: false, searchError: '' }
      : item));

    this.http.post<FoodItemAliasResponse>('/api/food-items/aliases', {
      originalName: row.originalGeminiName,
      canonicalName,
    }).subscribe({
      next: (response) => {
        this.items.update((rows) => rows.map((item) => item.id === rowId
          ? {
              ...item,
              'item name': response.canonicalName,
              aliasMatched: true,
              foodItemId: response.foodItemId,
              draftCanonicalName: response.canonicalName,
              isEditing: false,
              isSaving: false,
              editError: '',
              suggestions: [],
              isSearching: false,
              searchCompleted: false,
              searchError: '',
            }
          : item));
      },
      error: (response: HttpErrorResponse) => {
        const detail = response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'The correction could not be saved. Try again.';
        this.items.update((rows) => rows.map((item) => item.id === rowId
          ? { ...item, isSaving: false, editError: detail }
          : item));
      },
    });
  }

  protected canSavePurchases(): boolean {
    const rows = this.items();
    return rows.length > 0 &&
      this.receiptHash() !== null &&
      rows.every((row) => typeof row.foodItemId === 'string' && row.foodItemId.length > 0 && !row.isEditing && !row.isSaving) &&
      !this.isLoading() &&
      !this.isSavingPurchases() &&
      !this.purchasesSaved();
  }

  protected purchaseButtonLabel(): string {
    if (this.isSavingPurchases()) {
      return 'Saving purchases...';
    }

    if (this.duplicateReceipt()) {
      return 'Duplicate receipt - no items added';
    }

    const savedCount = this.savedPurchaseCount();
    if (savedCount !== null) {
      return `Saved ${savedCount} ${savedCount === 1 ? 'item' : 'items'} to purchases`;
    }

    return 'Save purchases';
  }

  protected savePurchases(): void {
    const hash = this.receiptHash();
    const rows = this.items();
    if (!hash || !this.canSavePurchases()) {
      return;
    }

    this.isSavingPurchases.set(true);
    this.purchaseError.set('');

    this.http.post<PurchaseSaveResponse>('/api/purchases', {
      hash,
      items: rows.map((row) => ({
        foodItemId: row.foodItemId!,
        dateOfPurchase: row['date of purchase'],
        quantityPurchased: row['quantity purchased'],
        costPerItem: row['cost per item'],
      })),
    }).subscribe({
      next: (response) => {
        this.isSavingPurchases.set(false);
        this.purchasesSaved.set(true);
        this.duplicateReceipt.set(response.alreadySaved);
        this.savedPurchaseCount.set(response.itemsSaved);
        const imageInput = this.document.getElementById('receipt-image') as HTMLInputElement | null;
        if (imageInput) {
          imageInput.value = '';
        }
        this.selectedFile.set(null);
      },
      error: (response: HttpErrorResponse) => {
        const detail = response.error && typeof response.error.detail === 'string'
          ? response.error.detail
          : 'Purchases could not be saved. Try again.';
        this.isSavingPurchases.set(false);
        this.purchaseError.set(detail);
      },
    });
  }

  @HostListener('document:keydown.enter', ['$event'])
  protected handleSavePurchasesEnter(event: Event): void {
    if (this.activePage() !== 'receiptUploads' || !this.canSavePurchases()) {
      return;
    }

    const target = event.target;
    if (target instanceof HTMLElement && target.closest('button, input, select, textarea, [contenteditable="true"]')) {
      return;
    }

    event.preventDefault();
    this.savePurchases();
  }

  protected formatAmount(amount: number): string {
    return amount.toFixed(2);
  }

  protected formatFileSize(size: number): string {
    return `${(size / (1024 * 1024)).toFixed(2)} MB`;
  }

  private createManualRow(): ManualRow {
    return {
      id: this.nextRowId++,
      draftName: '',
      foodItemName: null,
      foodItemId: null,
      isEditing: true,
      isSaving: false,
      editError: '',
      suggestions: [],
      isSearching: false,
      activeSuggestionIndex: 0,
      searchCompleted: false,
      searchError: '',
      quantity: '',
      purchaseDate: this.todayIsoDate(),
      costPerItem: '',
    };
  }

  private updateManualRow(rowId: number, update: (row: ManualRow) => ManualRow): void {
    this.manualRows.update(rows => rows.map(row => row.id === rowId ? update(row) : row));
  }

  private todayIsoDate(): string {
    const today = new Date();
    return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  }

  private isBlankManualRow(row: ManualRow): boolean {
    return !row.foodItemId && !row.draftName.trim() && !row.quantity.trim() && !row.costPerItem.trim();
  }

  private isValidManualRow(row: ManualRow): boolean {
    const quantity = row.quantity.trim();
    const cost = row.costPerItem.trim();
    return !!row.foodItemId &&
      !row.isEditing &&
      !row.isSaving &&
      /^\d+(\.\d{1,3})?$/.test(quantity) && Number(quantity) > 0 &&
      /^\d+(\.\d{1,2})?$/.test(cost) &&
      /^\d{4}-\d{2}-\d{2}$/.test(row.purchaseDate);
  }

  private manualLineKey(line: { name: string; dateOfPurchase: string; quantityPurchased: number; costPerItem: number }): string {
    return JSON.stringify([line.name, line.dateOfPurchase, line.quantityPurchased, line.costPerItem]);
  }

  private async computeSha256Hex(value: string): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
    return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
  }

  private cancelFoodSearch(rowId: number): void {
    const timer = this.searchTimers.get(rowId);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.searchTimers.delete(rowId);
    }

    this.searchRequests.get(rowId)?.unsubscribe();
    this.searchRequests.delete(rowId);
  }

  private clearFoodSearches(): void {
    for (const rowId of this.searchTimers.keys()) {
      this.cancelFoodSearch(rowId);
    }
    for (const rowId of this.searchRequests.keys()) {
      this.cancelFoodSearch(rowId);
    }
  }
}
