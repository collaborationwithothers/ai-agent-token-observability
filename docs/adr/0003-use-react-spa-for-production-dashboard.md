# Use React SPA For Production Dashboard

Status: accepted

The Product Dashboard will be built as a React SPA with Vite and TypeScript, hosted as the Product Dashboard Azure Container App, and backed by the Product API as the only product backend contract. This deliberately does not carry forward the local-first Blazor dashboard choice because the production dashboard needs a browser-first SaaS UX, clear separation from the .NET backend services, and a frontend architecture that can evolve independently while Product API owns authorization, content access, and session investigation data.

Considered options were Blazor, Next.js, Angular, Vue, and React SPA. Blazor keeps .NET alignment but carries local-first assumptions forward. Next.js is a valid future option if server-side rendering or a frontend BFF becomes necessary, but it risks creating a second backend boundary too early. Angular and Vue are viable, but React SPA gives the smallest deliberate production step for a rich API-backed dashboard without changing the already-agreed Product API boundary.
