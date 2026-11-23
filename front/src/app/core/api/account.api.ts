import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AccountDto } from '../models/api-models';
import { apiUrl } from './api-url';

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  me(): Observable<AccountDto> {
    return this.http.get<AccountDto>(apiUrl('/api/account/me'));
  }
}
