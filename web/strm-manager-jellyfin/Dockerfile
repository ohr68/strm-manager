FROM node:24-alpine AS web-build

WORKDIR /src

ARG VITE_STRM_MANAGER_BASE_URL

ENV VITE_STRM_MANAGER_BASE_URL=${VITE_STRM_MANAGER_BASE_URL}

RUN corepack enable

COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY tsconfig.json vite.config.ts ./
COPY src ./src

RUN pnpm run typecheck \
    && pnpm run build


FROM jellyfin/jellyfin@sha256:78d3ea1207d1322471fcac39a614f004f2ccf7e878f95ab2977d752f07e4dd7e

COPY --from=web-build \
    /src/dist/strm-manager.js \
    /jellyfin/jellyfin-web/strm-manager/strm-manager.js

COPY --from=web-build \
    /src/dist/strm-manager-jellyfin.css \
    /jellyfin/jellyfin-web/strm-manager/strm-manager-jellyfin.css

COPY docker/patch-jellyfin-web.pl \
    /tmp/patch-jellyfin-web.pl

RUN perl /tmp/patch-jellyfin-web.pl \
    && test -f /jellyfin/jellyfin-web/strm-manager/strm-manager.js \
    && test -f /jellyfin/jellyfin-web/strm-manager/strm-manager-jellyfin.css \
    && test "$(grep -o 'strm-manager.js' /jellyfin/jellyfin-web/index.html | wc -l)" -eq 1 \
    && test "$(grep -o 'strm-manager-jellyfin.css' /jellyfin/jellyfin-web/index.html | wc -l)" -eq 1 \
    && rm /tmp/patch-jellyfin-web.pl