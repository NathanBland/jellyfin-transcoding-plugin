DOTNET ?= dotnet
SOLUTION := Jellyfin.Plugin.TranscodingPolicy.slnx

.PHONY: restore build test package clean

restore:
	$(DOTNET) restore $(SOLUTION)

build:
	$(DOTNET) build $(SOLUTION) --configuration Release --no-restore

test:
	$(DOTNET) test $(SOLUTION) --configuration Release --no-build

package:
	DOTNET=$(DOTNET) ./scripts/package.sh

clean:
	$(DOTNET) clean $(SOLUTION)

