export type AssessmentStatus = 'pending' | 'insufficientEvidence' | 'withinRange' | 'excursion';

export interface ShipmentSummary {
  id: string;
  code: string;
  route: string;
  readingVersion: number;
  evaluatedVersion: number;
  assessmentStatus: AssessmentStatus;
}

export interface ShipmentDetail extends ShipmentSummary {
  temperatureMin: number;
  temperatureMax: number;
  readings: { id: string; observedAt: string; receivedAt: string; celsius: number }[];
  excursion: { startedAt: string; detectedAt: string } | null;
  review: { decision: string; note: string; reviewedAt: string; evaluatedVersion: number } | null;
}

export const assessmentLabels: Record<AssessmentStatus, string> = {
  pending: 'Avaliação pendente',
  insufficientEvidence: 'Evidência insuficiente',
  withinRange: 'Dentro da faixa',
  excursion: 'Excursão confirmada',
};

export const assessmentHelp: Record<AssessmentStatus, string> = {
  pending: 'Há leituras novas aguardando processamento.',
  insufficientEvidence: 'Ainda não há evidência suficiente para concluir a avaliação.',
  withinRange: 'As leituras avaliadas estão dentro da faixa prevista.',
  excursion: 'A avaliação confirmou um desvio de temperatura.',
};

export function reviewState(detail: ShipmentDetail): 'none' | 'current' | 'stale' {
  if (!detail.review) return 'none';
  return detail.review.evaluatedVersion === detail.evaluatedVersion ? 'current' : 'stale';
}

export function canReview(detail: ShipmentDetail): boolean {
  return detail.assessmentStatus === 'excursion' && !!detail.excursion && reviewState(detail) !== 'current';
}
