# Disposable workspace for the QA test agent: Codex CLI, qa-run and common toolchains.
# Other languages and versions are installed on demand by the agent through mise.
# Build: docker build -t qa-agent/test-runner:1 docker/test-agent
FROM debian:bookworm-slim

ENV DEBIAN_FRONTEND=noninteractive \
    MISE_DATA_DIR=/opt/mise \
    MISE_YES=1 \
    PATH=/opt/mise/shims:/root/.dotnet:/root/.dotnet/tools:$PATH \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update && apt-get install -y --no-install-recommends \
      bash ca-certificates curl git gnupg jq unzip xz-utils zip build-essential pkg-config \
      libssl-dev libicu-dev libffi-dev zlib1g-dev libsqlite3-dev procps util-linux \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://mise.run | MISE_INSTALL_PATH=/usr/local/bin/mise sh \
    && mise use -g node@22 python@3.12 java@temurin-21 go@1.24 \
    && mise reshim

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /root/.dotnet \
    && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir /root/.dotnet \
    && rm /tmp/dotnet-install.sh

RUN npm install -g @openai/codex@0.163.0 && mise reshim

COPY qa-run /usr/local/bin/qa-run
WORKDIR /src
