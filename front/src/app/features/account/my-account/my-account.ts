import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { AccountApi } from '../../../core/api/account.api';
import { toApiError } from '../../../core/errors/api-error';
import { DAY_FORMAT, DETAIL_DATE_FORMAT } from '../../../core/time/date-formats';
import { formatTunisianPhone } from '../../../shared/phone';
import { Loading } from '../../../shared/ui/loading';

/** Mon compte (US-F3, lecture seule en semaine 2 ; suppression du compte avec US-F2). */
@Component({
  selector: 'app-my-account',
  imports: [DatePipe, MatButtonModule, Loading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './my-account.html',
  styleUrl: './my-account.scss',
})
export class MyAccount {
  private readonly api = inject(AccountApi);

  // rxResource : lance l'appel à la création du composant et expose value(), isLoading(), error(), reload()
  // sous forme de signals. Pas de params : la requête ne dépend d'aucun signal, elle part une fois.
  protected readonly account = rxResource({ stream: () => this.api.me() });

  // Piège : value() LÈVE une exception quand la ressource est en erreur ; le template teste error() d'abord.
  protected readonly errorMessage = computed(() => {
    const error = this.account.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly formatPhone = formatTunisianPhone;
  protected readonly dayFormat = DAY_FORMAT;
  protected readonly detailFormat = DETAIL_DATE_FORMAT;
}
