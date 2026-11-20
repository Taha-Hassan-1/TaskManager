import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup } from '@angular/forms';
import { toProblem } from './errors';
import { applyServerErrors, safeReturnUrl } from './forms';

describe('toProblem', () => {
  it('reads a validation problem, lower-casing field names to match the forms', () => {
    const problem = toProblem(new HttpErrorResponse({ status: 400, error: { title: 'One or more validation errors occurred.', errors: { Password: ['Too short.'] } } }));
    expect(problem).toEqual({ status: 400, message: 'Too short.', fieldErrors: { password: ['Too short.'] } });
  });

  it('prefers the detail of a plain problem, then its title', () => {
    expect(toProblem(new HttpErrorResponse({ status: 409, error: { title: 'Conflict', detail: 'Reload the card and try again.' } })).message).toBe('Reload the card and try again.');
    expect(toProblem(new HttpErrorResponse({ status: 409, error: { title: 'Email is already registered.' } })).message).toBe('Email is already registered.');
  });

  it('explains a network failure', () => {
    expect(toProblem(new HttpErrorResponse({ status: 0 })).message).toContain('Cannot reach the server');
  });

  it('copes with a body that is not a problem at all', () => {
    expect(toProblem(new HttpErrorResponse({ status: 502, error: '<html>Bad gateway</html>' })).message).toBe('The server answered 502.');
    expect(toProblem(new Error('boom')).message).toBe('Something went wrong.');
  });
});

describe('applyServerErrors', () => {
  it('puts messages on the matching fields and returns the rest', () => {
    const form = new FormGroup({ email: new FormControl(''), password: new FormControl('') });

    const unmatched = applyServerErrors(form, { email: ['Taken.'], other: ['Something else.'] });

    expect(form.controls.email.getError('server')).toBe('Taken.');
    expect(form.controls.password.errors).toBeNull();
    expect(unmatched).toEqual(['Something else.']);
  });
});

describe('safeReturnUrl', () => {
  it('keeps same-site paths', () => expect(safeReturnUrl('/boards/3?x=1')).toBe('/boards/3?x=1'));

  it.each(['https://evil.example/', '//evil.example/', 'javascript:alert(1)', '', null])('refuses %s', (url) => {
    expect(safeReturnUrl(url)).toBe('/boards');
  });
});
