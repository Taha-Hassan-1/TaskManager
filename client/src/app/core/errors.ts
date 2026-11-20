import { HttpErrorResponse } from '@angular/common/http';

/** What a failed API call means for the user, taken from the server's ProblemDetails. */
export interface ApiProblem {
  status: number;
  message: string;
  /** Validation messages by field name (camelCase, as the API reports them). */
  fieldErrors: Record<string, string[]>;
}

export function toProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return { status: 0, message: 'Cannot reach the server. Check your connection and try again.', fieldErrors: {} };
    }
    const body = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
    const fieldErrors = normalizeFieldNames(body?.errors ?? {});
    const firstField = Object.values(fieldErrors)[0]?.[0];
    return {
      status: error.status,
      message: firstField ?? body?.detail ?? body?.title ?? `The server answered ${error.status}.`,
      fieldErrors,
    };
  }
  return { status: -1, message: 'Something went wrong.', fieldErrors: {} };
}

/** ASP.NET reports "Password", the forms use "password". */
function normalizeFieldNames(errors: Record<string, string[]>): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  for (const [key, messages] of Object.entries(errors)) {
    result[key.charAt(0).toLowerCase() + key.slice(1)] = messages;
  }
  return result;
}
