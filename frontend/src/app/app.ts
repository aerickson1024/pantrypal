import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, inject, Injector, OnDestroy, signal } from '@angular/core';
import { Subscription } from 'rxjs';

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
})
export class App implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private nextRowId = 0;
  private readonly searchTimers = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly searchRequests = new Map<number, Subscription>();
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly items = signal<ReceiptRow[]>([]);
  protected readonly errorMessage = signal('');
  protected readonly isLoading = signal(false);
  protected readonly receiptHash = signal<string | null>(null);
  protected readonly isSavingPurchases = signal(false);
  protected readonly purchasesSaved = signal(false);
  protected readonly purchaseStatus = signal('');
  protected readonly purchaseError = signal('');

  ngOnDestroy(): void {
    this.clearFoodSearches();
  }

  protected selectImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.clearFoodSearches();
    this.items.set([]);
    this.errorMessage.set('');
    this.receiptHash.set(null);
    this.purchasesSaved.set(false);
    this.purchaseStatus.set('');
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
    this.purchaseStatus.set('');
    this.purchaseError.set('');
    this.isLoading.set(true);

    this.http.post<ReceiptParseResponse>('http://localhost:5044/api/gemini/receipts/parse', formData).subscribe({
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
        'http://localhost:5044/api/food-items/search',
        { params: { q: query } },
      ).subscribe({
        next: (suggestions) => {
          this.searchRequests.delete(rowId);
          this.items.update((rows) => rows.map((row) => row.id === rowId && row.isEditing && row.draftCanonicalName.trim() === query
            ? { ...row, suggestions, isSearching: false, activeSuggestionIndex: 0, searchError: '' }
            : row));
        },
        error: () => {
          this.searchRequests.delete(rowId);
          this.items.update((rows) => rows.map((row) => row.id === rowId && row.draftCanonicalName.trim() === query
            ? { ...row, suggestions: [], isSearching: false, searchError: 'Food suggestions could not be loaded.' }
            : row));
        },
      });
      this.searchRequests.set(rowId, request);
    }, 500);
    this.searchTimers.set(rowId, timer);
  }

  protected handleSuggestionKeydown(rowId: number, event: KeyboardEvent): void {
    const row = this.items().find((item) => item.id === rowId);
    if (!row?.suggestions.length) {
      if (event.key === 'Escape') {
        this.cancelFoodSearch(rowId);
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
    } else if (event.key === 'Enter') {
      event.preventDefault();
      this.selectSuggestion(rowId, row.suggestions[row.activeSuggestionIndex]);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.items.update((rows) => rows.map((item) => item.id === rowId
        ? { ...item, suggestions: [], activeSuggestionIndex: 0 }
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
      ? { ...item, isSaving: true, editError: '', suggestions: [], isSearching: false, searchError: '' }
      : item));

    this.http.post<FoodItemAliasResponse>('http://localhost:5044/api/food-items/aliases', {
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

    return this.purchasesSaved() ? 'Purchases saved' : 'Save purchases';
  }

  protected savePurchases(): void {
    const hash = this.receiptHash();
    const rows = this.items();
    if (!hash || !this.canSavePurchases()) {
      return;
    }

    this.isSavingPurchases.set(true);
    this.purchaseStatus.set('');
    this.purchaseError.set('');

    this.http.post<PurchaseSaveResponse>('http://localhost:5044/api/purchases', {
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
        this.purchaseStatus.set(response.alreadySaved
          ? 'This receipt was already saved.'
          : `Saved ${response.itemsSaved} purchase items.`);
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

  protected formatAmount(amount: number): string {
    return amount.toFixed(2);
  }

  protected formatFileSize(size: number): string {
    return `${(size / (1024 * 1024)).toFixed(2)} MB`;
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
