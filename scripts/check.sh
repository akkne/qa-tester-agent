#!/usr/bin/env bash
set -euo pipefail

dotnet restore QaAgent.slnx -m:1 -p:BuildInParallel=false
dotnet format whitespace QaAgent.slnx --verify-no-changes --no-restore
dotnet format style QaAgent.slnx --verify-no-changes --no-restore
dotnet build QaAgent.slnx --no-restore -warnaserror -m:1 -p:BuildInParallel=false
dotnet tool restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --no-build --project src/QaAgent.Infrastructure --startup-project src/QaAgent.Host --context RunDbContext
python3 scripts/check_architecture.py
dotnet test QaAgent.slnx --no-build --no-restore
bash scripts/check_m1.sh
