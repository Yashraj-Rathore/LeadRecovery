FROM node:26.5.0-bookworm-slim@sha256:2d49d876e96237d76de412761cf05dbfe5aee325cc4406a4d41d5824c5bb8beb AS dependencies
WORKDIR /workspace

RUN corepack enable && corepack prepare pnpm@11.10.0 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
COPY src/LeadRecovery.Web/package.json src/LeadRecovery.Web/package.json
COPY tests/LeadRecovery.E2E/package.json tests/LeadRecovery.E2E/package.json
RUN pnpm install --frozen-lockfile --filter @leadrecovery/web...

FROM dependencies AS build
ARG API_BASE_URL=http://leadrecovery-api:8080
ENV API_BASE_URL=$API_BASE_URL \
    NEXT_TELEMETRY_DISABLED=1
COPY src/LeadRecovery.Web/ src/LeadRecovery.Web/
RUN pnpm --filter @leadrecovery/web build

FROM node:26.5.0-alpine3.23@sha256:0473b6671ff22c8eeb570c0e1e51408595d3171e73f8002c269b763f0a943149 AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
ARG CREATED=unknown
LABEL org.opencontainers.image.title="LeadRecovery Web" \
      org.opencontainers.image.description="LeadRecovery staff dashboard" \
      org.opencontainers.image.source="https://github.com/Yashraj-Rathore/LeadRecovery" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.created="$CREATED"

# The standalone Next.js server needs Node.js only. Remove package managers and
# their dependency trees from the final image to minimize runtime attack surface.
RUN rm -rf /opt/yarn-v* /usr/local/lib/node_modules/corepack /usr/local/lib/node_modules/npm \
    && rm -f /usr/local/bin/corepack /usr/local/bin/npm /usr/local/bin/npx \
      /usr/local/bin/pnpm /usr/local/bin/pnpx /usr/local/bin/yarn /usr/local/bin/yarnpkg

WORKDIR /app
COPY --from=build --chown=node:node /workspace/src/LeadRecovery.Web/.next/standalone ./

ENV API_BASE_URL=http://leadrecovery-api:8080 \
    HOSTNAME=0.0.0.0 \
    NEXT_TELEMETRY_DISABLED=1 \
    NODE_ENV=production \
    PORT=3000
EXPOSE 3000
USER node
WORKDIR /app/src/LeadRecovery.Web
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["node", "-e", "fetch('http://127.0.0.1:3000/').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"]
CMD ["node", "server.js"]
