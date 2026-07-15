DOTNET ?= dotnet
SOLUTION := Jellyfin.Plugin.RecordingTranscoder.slnx
VERSION := $(shell sed -n 's/^version: *"\([^"].*\)".*/\1/p' build.yaml)
ARCHIVE := artifacts/recording-transcoder_$(VERSION).zip
.PHONY: restore build test package validate-package ci clean
restore:
	$(DOTNET) restore $(SOLUTION)
build:
	$(DOTNET) build $(SOLUTION) --configuration Release --no-restore
test:
	$(DOTNET) test $(SOLUTION) --configuration Release --no-build
package:
	DOTNET=$(DOTNET) ./scripts/package.sh
validate-package:
	python3 scripts/validate_package.py --archive $(ARCHIVE)
ci: restore build test package validate-package
clean:
	$(DOTNET) clean $(SOLUTION)
