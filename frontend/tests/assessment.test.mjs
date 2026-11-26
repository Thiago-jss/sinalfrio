import assert from 'node:assert/strict';
import { test } from 'node:test';
import { canReview, reviewState, assessmentLabels } from '../src/assessment.ts';

const detail = { assessmentStatus: 'excursion', evaluatedVersion: 4, excursion: {}, review: {evaluatedVersion: 4} };

test('a review belongs to exactly one evaluation, even when readings are pending', () => {
  assert.equal(reviewState({...detail, assessmentStatus: 'pending', readingVersion: 5}), 'current');
  assert.equal(reviewState({...detail, evaluatedVersion: 5}), 'stale');
  assert.equal(reviewState({...detail, review: null}), 'none');
});

test('review only an unreviewed, confirmed excursion for the current evaluation', () => {
  assert.equal(canReview(detail), false);
  assert.equal(canReview({...detail, review: null}), true);
  assert.equal(canReview({...detail, evaluatedVersion: 5}), true);
  assert.equal(canReview({...detail, review: null, assessmentStatus: 'pending'}), false);
  assert.equal(canReview({...detail, review: null, assessmentStatus: 'withinRange'}), false);
  assert.equal(canReview({...detail, review: null, excursion: null}), false);
});

test('the current assessment labels are distinct', () => {
  assert.equal(new Set(Object.values(assessmentLabels)).size, 4);
});
