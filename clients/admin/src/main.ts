import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { initBrowserTelemetry } from './admin-kit';

initBrowserTelemetry('admin-client');

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
