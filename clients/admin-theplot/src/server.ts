import './instrumentation';

import { createNodeRequestHandler, isMainModule } from '@angular/ssr/node';
import { join } from 'node:path';
import { createAdminServer } from './admin-kit/server';

const app = createAdminServer({
  browserDistFolder: join(import.meta.dirname, '../browser'),
  basePath: '/theplot/',
});

if (isMainModule(import.meta.url)) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

export const reqHandler = createNodeRequestHandler(app);
