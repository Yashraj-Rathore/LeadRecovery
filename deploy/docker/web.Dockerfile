FROM node:24.18.0-bookworm-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d AS dependencies
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

FROM node:24.18.0-bookworm-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
ARG CREATED=unknown
LABEL org.opencontainers.image.title="LeadRecovery Web" \
      org.opencontainers.image.description="LeadRecovery staff dashboard" \
      org.opencontainers.image.source="https://github.com/Yashraj-Rathore/LeadRecovery" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION" \
      org.opencontainers.image.created="$CREATED"

WORKDIR /app
COPY --from=build --chown=node:node /workspace/src/LeadRecovery.Web/.next/standalone ./
COPY --from=build --chown=node:node /workspace/src/LeadRecovery.Web/.next/static ./src/LeadRecovery.Web/.next/static

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
