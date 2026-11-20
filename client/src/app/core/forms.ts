import { AbstractControl, FormGroup } from '@angular/forms';

/** Shows the server's validation messages on the matching form fields. Returns messages that fit no field. */
export function applyServerErrors(form: FormGroup, fieldErrors: Record<string, string[]>): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(fieldErrors)) {
    const control: AbstractControl | null = form.get(field);
    if (control) control.setErrors({ server: messages[0] });
    else unmatched.push(...messages);
  }
  return unmatched;
}

/** Only same-site paths, so a crafted ?returnUrl cannot send someone to another site after signing in. */
export function safeReturnUrl(url: string | null): string {
  return url && url.startsWith('/') && !url.startsWith('//') ? url : '/boards';
}
