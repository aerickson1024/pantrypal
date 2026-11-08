import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';

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
}

interface ReceiptParseResponse {
  items: ReceiptItem[];
  hash: string;
}

interface FoodItemAliasResponse {
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
  template: `
    <main>
      <header>
        <h1>PantryPal</h1>
        <p>Receipt items</p>
      </header>

      <section class="results" aria-live="polite" [attr.aria-busy]="isLoading()">
        @if (items().length) {
          <div class="table-scroll">
            <table>
              <caption>Items found on the receipt</caption>
              <thead>
                <tr>
                  <th scope="col">Item name</th>
                  <th scope="col">Quantity</th>
                  <th scope="col">Purchase date</th>
                  <th scope="col" class="numeric">Cost per item</th>
                </tr>
              </thead>
              <tbody>
                @for (item of items(); track item.id) {
                  <tr>
                    <td>
                      @if (item.aliasMatched) {
                        <span>{{ item['item name'] }}</span>
                      } @else if (item.isEditing) {
                        <div class="item-editor">
                          <input
                            type="text"
                            maxlength="255"
                            [value]="item.draftCanonicalName"
                            [disabled]="item.isSaving"
                            [attr.aria-label]="'Canonical food name for ' + item.originalGeminiName"
                            (input)="updateDraft(item.id, $event)"
                          />
                          <button
                            type="button"
                            class="item-action item-action--confirm"
                            title="Save correction"
                            [attr.aria-label]="'Save correction for ' + item.originalGeminiName"
                            [disabled]="!canSave(item)"
                            (click)="saveCorrection(item.id)"
                          >&#10003;</button>
                          <button
                            type="button"
                            class="item-action"
                            title="Cancel correction"
                            [attr.aria-label]="'Cancel correction for ' + item.originalGeminiName"
                            [disabled]="item.isSaving"
                            (click)="cancelEdit(item.id)"
                          >&times;</button>
                        </div>
                        @if (item.editError) {
                          <p class="item-edit-error" role="alert">{{ item.editError }}</p>
                        }
                      } @else {
                        <div class="item-name-view">
                          <button
                            type="button"
                            class="item-action"
                            title="Edit food name"
                            [attr.aria-label]="'Edit food name for ' + item.originalGeminiName"
                            (click)="startEditing(item.id)"
                          >&#9998;</button>
                          <span>{{ item['item name'] }}</span>
                        </div>
                      }
                    </td>
                    <td>{{ item['quantity purchased'] }}</td>
                    <td>{{ item['date of purchase'] }}</td>
                    <td class="numeric">\${{ formatAmount(item['cost per item']) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="purchase-action">
            <button
              type="button"
              class="save-purchases"
              [disabled]="!canSavePurchases()"
              (click)="savePurchases()"
            >{{ purchaseButtonLabel() }}</button>
          </div>
        } @else if (!isLoading() && !errorMessage()) {
          <p class="empty-state">No receipt parsed yet.</p>
        }
        @if (isLoading()) {
          <p class="status" role="status">Reading receipt...</p>
        }
        @if (errorMessage()) {
          <p class="error" role="alert">{{ errorMessage() }}</p>
        }
        @if (purchaseStatus()) {
          <p class="status purchase-status" role="status">{{ purchaseStatus() }}</p>
        }
        @if (purchaseError()) {
          <p class="error" role="alert">{{ purchaseError() }}</p>
        }
      </section>

      <form class="composer" (submit)="submitReceipt($event)">
        <label for="receipt-image">Receipt image</label>
        <div class="upload-row">
          <input
            id="receipt-image"
            name="image"
            type="file"
            accept="image/jpeg,image/png,image/webp,image/heic,image/heif"
            [disabled]="isLoading() || isSavingPurchases()"
            (change)="selectImage($event)"
          />
          <button type="submit" [disabled]="isLoading() || isSavingPurchases() || !selectedFile()">
            {{ isLoading() ? 'Parsing...' : 'Parse receipt' }}
          </button>
        </div>
        @if (selectedFile(); as file) {
          <p class="file-name">{{ file.name }} · {{ formatFileSize(file.size) }}</p>
        }
      </form>
    </main>
  `,
})
export class App {
  private readonly http = inject(HttpClient);
  private nextRowId = 0;
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly items = signal<ReceiptRow[]>([]);
  protected readonly errorMessage = signal('');
  protected readonly isLoading = signal(false);
  protected readonly receiptHash = signal<string | null>(null);
  protected readonly isSavingPurchases = signal(false);
  protected readonly purchasesSaved = signal(false);
  protected readonly purchaseStatus = signal('');
  protected readonly purchaseError = signal('');

  protected selectImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
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
  }

  protected updateDraft(rowId: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? { ...row, draftCanonicalName: input.value, editError: '' }
      : row));
  }

  protected canSave(row: ReceiptRow): boolean {
    return !row.isSaving && row.draftCanonicalName.trim().length > 0 && row.draftCanonicalName.trim().length <= 255;
  }

  protected cancelEdit(rowId: number): void {
    this.items.update((rows) => rows.map((row) => row.id === rowId
      ? { ...row, isEditing: false, draftCanonicalName: row['item name'], editError: '' }
      : row));
  }

  protected saveCorrection(rowId: number): void {
    const row = this.items().find((item) => item.id === rowId);
    if (!row || !this.canSave(row)) {
      return;
    }

    const canonicalName = row.draftCanonicalName.trim();
    this.items.update((rows) => rows.map((item) => item.id === rowId
      ? { ...item, isSaving: true, editError: '' }
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
}
