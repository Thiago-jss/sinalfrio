import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Subscription, timer } from 'rxjs';
import { ShipmentsApi } from './api';
import { assessmentHelp, assessmentLabels, canReview, reviewState } from './assessment';
import type { ShipmentSummary, ShipmentDetail } from './assessment';

@Component({
  selector: 'sf-root',
  standalone: true,
  imports: [DatePipe, DecimalPipe],
  templateUrl: './panel.html',
})
export class OperationsPanel implements OnDestroy {
  private readonly api = inject(ShipmentsApi);
  private polls?: Subscription;
  private listRequest?: Subscription;
  private detailRequest?: Subscription;
  private reviewRequest?: Subscription;
  private readonly drafts = new Map<string, string>();

  readonly labels = assessmentLabels;
  readonly help = assessmentHelp;
  readonly reviewState = reviewState;
  readonly canReview = canReview;
  readonly phase = signal<'disconnected' | 'loading' | 'connected' | 'unauthorized' | 'error'>('disconnected');
  readonly active = computed(() => this.phase() === 'connected' || this.phase() === 'loading');
  readonly shipments = signal<ShipmentSummary[]>([]);
  readonly selected = signal<string | null>(null);
  readonly detail = signal<ShipmentDetail | null>(null);
  readonly listError = signal('');
  readonly detailError = signal('');
  readonly reviewMessage = signal('');
  readonly saving = signal(false);
  readonly note = signal('');
  readonly refreshed = signal<Date | null>(null);
  readonly rows = computed(() => [...(this.detail()?.readings ?? [])].sort(
    (a, b) => Date.parse(b.observedAt) - Date.parse(a.observedAt) || Date.parse(b.receivedAt) - Date.parse(a.receivedAt),
  ));

  connect(input: HTMLInputElement) {
    const token = input.value.trim();
    if (!token || this.active()) return;
    this.stop('disconnected');
    this.api.connect(token);
    input.value = '';
    this.phase.set('loading');
    this.polls = timer(0, 4000).subscribe(() => {
      this.loadList();
      this.loadDetail();
    });
  }

  stop(phase: 'disconnected' | 'unauthorized' | 'error' = 'disconnected') {
    this.polls?.unsubscribe();
    this.listRequest?.unsubscribe();
    this.detailRequest?.unsubscribe();
    this.reviewRequest?.unsubscribe();
    this.api.disconnect();
    this.phase.set(phase);
    this.shipments.set([]);
    this.selected.set(null);
    this.detail.set(null);
    this.listError.set('');
    this.detailError.set('');
    this.reviewMessage.set('');
    this.saving.set(false);
    this.note.set('');
    this.refreshed.set(null);
    this.drafts.clear();
  }

  ngOnDestroy() { this.stop(); }

  select(id: string | null) {
    if (id === this.selected() || this.saving()) return;
    this.detailRequest?.unsubscribe();
    this.selected.set(id);
    this.detail.set(null);
    this.detailError.set('');
    this.reviewMessage.set('');
    this.note.set(id ? this.drafts.get(id) ?? '' : '');
    this.loadDetail();
  }

  edit(note: string) {
    this.note.set(note);
    const id = this.selected();
    if (id) this.drafts.set(id, note);
  }

  loadList() {
    if (!this.active() || this.listRequest && !this.listRequest.closed) return;
    this.listRequest = this.api.list().subscribe({
      next: shipments => {
        this.shipments.set(shipments);
        this.listError.set('');
        this.refreshed.set(new Date());
        this.phase.set('connected');
        if (!shipments.some(s => s.id === this.selected())) this.select(shipments[0]?.id ?? null);
      },
      error: (error: HttpErrorResponse) => {
        if (this.rejectAuth(error)) return;
        if (this.phase() === 'loading') this.stop('error');
        else this.listError.set('Não foi possível atualizar a lista. Nova tentativa automática.');
      },
    });
  }

  loadDetail() {
    const id = this.selected();
    if (!id || this.saving() || this.detailRequest && !this.detailRequest.closed) return;
    this.detailRequest = this.api.detail(id).subscribe({
      next: detail => {
        this.detail.set(detail);
        this.detailError.set('');
      },
      error: (error: HttpErrorResponse) => {
        if (!this.rejectAuth(error)) this.detailError.set('Não foi possível atualizar a remessa. Nova tentativa automática.');
      },
    });
  }

  submit(detail: ShipmentDetail) {
    const note = this.note().trim();
    if (this.saving() || this.detailError() || !note || !canReview(detail) || this.detail() !== detail) return;
    this.detailRequest?.unsubscribe();
    this.saving.set(true);
    this.reviewMessage.set('');
    this.reviewRequest = this.api.review(detail.id, detail.evaluatedVersion, note).subscribe({
      next: updated => {
        this.detail.set(updated);
        this.edit('');
        this.saving.set(false);
        this.loadList();
      },
      error: (error: HttpErrorResponse) => {
        if (this.rejectAuth(error)) return;
        if (error.status === 409) {
          this.reviewMessage.set('A avaliação mudou antes do registro. Confira a versão atualizada; sua justificativa foi preservada.');
          this.detailError.set('Atualizando a avaliação…');
          this.saving.set(false);
          this.loadDetail();
          this.loadList();
        } else {
          this.saving.set(false);
          this.reviewMessage.set('Não foi possível confirmar o registro. Confira a remessa antes de tentar novamente.');
          this.detailError.set('Atualizando a avaliação…');
          this.loadDetail();
        }
      },
    });
  }

  private rejectAuth(error: HttpErrorResponse): boolean {
    if (error.status !== 401 && error.status !== 403) return false;
    this.stop('unauthorized');
    return true;
  }
}
