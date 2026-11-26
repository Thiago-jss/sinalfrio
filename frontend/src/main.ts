import { provideHttpClient } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { LOCALE_ID } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { OperationsPanel } from './panel';

registerLocaleData(localePt);
bootstrapApplication(OperationsPanel, {
  providers: [provideHttpClient(), { provide: LOCALE_ID, useValue: 'pt-BR' }],
}).catch(() => console.error('Não foi possível iniciar o painel.'));
