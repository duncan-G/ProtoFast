import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  provideRouter,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { AuthIdentityService, type AuthIdentity } from './auth-identity';
import { admits, CONSOLE_ROLES, consoleGuard, roleGuard } from './console-access';

describe('console access', () => {
  function signedInAs(roles: string[], consoleRoles: string[] = ['admin-theplot']): void {
    const identity: AuthIdentity = { authenticated: true, userId: 'op', tenant: 'operators', roles };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: CONSOLE_ROLES, useValue: consoleRoles },
        {
          provide: AuthIdentityService,
          useValue: {
            identity,
            authenticated: true,
            hasRole: (role: string) => roles.includes(role),
          },
        },
      ],
    });
  }

  function run(guard: typeof consoleGuard): ReturnType<typeof consoleGuard> {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url: '/stories' } as RouterStateSnapshot),
    );
  }

  it('admits any one of the console roles, or anyone for *', () => {
    expect(admits(['admin-theplot'], ['offline_access', 'admin-theplot'])).toBe(true);
    expect(admits(['admin-theplot'], ['admin-protofast', 'platform'])).toBe(false);
    expect(admits(['*'], [])).toBe(true);
  });

  it('lets a holder of the console role in', () => {
    signedInAs(['admin-theplot']);
    expect(run(consoleGuard)).toBe(true);
  });

  it('sends another app’s operator to the forbidden page', () => {
    signedInAs(['admin-protofast', 'platform']);
    const result = run(consoleGuard);
    expect(result instanceof UrlTree).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/forbidden');
  });

  it('keeps platform pages to the platform role', () => {
    signedInAs(['admin-theplot'], ['*']);
    expect(run(consoleGuard)).toBe(true);
    expect(run(roleGuard('platform')) instanceof UrlTree).toBe(true);
  });
});
