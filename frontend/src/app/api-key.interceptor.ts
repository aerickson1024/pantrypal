import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { ApiKeyService, apiKeyHeaderName, verifyApiKeyUrl } from './api-key.service';

export const apiKeyInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/')) {
    return next(request);
  }

  const apiKey = inject(ApiKeyService);
  const key = apiKey.key();
  const outgoing = key && !request.headers.has(apiKeyHeaderName)
    ? request.clone({ setHeaders: { [apiKeyHeaderName]: key } })
    : request;

  return next(outgoing).pipe(
    tap({
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401 && request.url !== verifyApiKeyUrl) {
          apiKey.markInvalid();
        }
      },
    }),
  );
};
