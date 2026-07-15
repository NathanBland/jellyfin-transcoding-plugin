DOTNET ?= dotnet
PLUGIN ?= transcoding-policy
PLUGIN_DIR := plugins/$(PLUGIN)

.PHONY: restore build test test-release-tools package validate-package ci clean

restore build test test-release-tools package validate-package ci clean:
	$(MAKE) -C $(PLUGIN_DIR) $@ DOTNET=$(DOTNET)
