// The hint under a review request's due date (request_architecture_review, request_security_review): an empty date
// takes the days the rules in force set (setting review-request-due-days, wave 4b review). It renders inside an open sheet
// only, so the setting is read when a sheet opens, never by a page that only shows the button.
import { useSettingValue } from '../data/reads';
import { reviewDueHint } from '../data/format';

export const REVIEW_DUE_DAYS_SETTING = 'review-request-due-days';

export function ReviewDueHint() {
  return <>{reviewDueHint(useSettingValue(REVIEW_DUE_DAYS_SETTING))}</>;
}
