DOTNET ?= dotnet
SOLUTION := Jellyfin.Plugin.TranscodingPolicy.slnx
VERSION := $(shell sed -n 's/^version: *"\([^"].*\)".*/\1/p' build.yaml)
ARCHIVE := artifacts/transcoding-policy_$(VERSION).zip

.PHONY: restore build test test-release-tools package validate-package ci clean

restore:
	$(DOTNET) restore $(SOLUTION)

build:
	$(DOTNET) build $(SOLUTION) --configuration Release --no-restore

test:
	$(DOTNET) test $(SOLUTION) --configuration Release --no-build

test-release-tools:
	python3 -m unittest scripts.tests.test_update_manifest

package:
	DOTNET=$(DOTNET) ./scripts/package.sh

validate-package:
	python3 scripts/validate_package.py --archive $(ARCHIVE)

ci: restore build test test-release-tools package validate-package

clean:
	$(DOTNET) clean $(SOLUTION)
