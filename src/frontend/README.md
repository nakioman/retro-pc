# RetroBox frontend

The Vite development server proxies `/api` to `http://localhost:8080` by
default, so `npm run dev` works with the local RetroBox host without CORS
configuration.

Copy `.env.example` to `.env.local` to change either endpoint:

- `VITE_API_PROXY_TARGET` changes the API target used only by the Vite dev server.
- `VITE_API_BASE_URL` prefixes API URLs in the compiled bundle. Leave it empty
  when the web host and API use the same origin, which is the appliance default.

Run `npm run dev` from this directory for development. Run `npm run build` to
type-check and emit the embedded assets into `../RetroBox.Web/wwwroot`.
