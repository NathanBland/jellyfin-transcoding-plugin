from __future__ import annotations

import unittest

from scripts.update_manifest import update_manifest


METADATA = {
    "guid": "cf76f48b-2da2-46b7-9237-3c0fd14662e6",
    "name": "Transcoding Policy",
    "description": "Description",
    "overview": "Overview",
    "owner": "nathanbland",
    "category": "General",
    "targetAbi": "10.11.11.0",
}


def release(version: str, checksum: str = "a" * 32) -> dict[str, str]:
    return {
        "version": version,
        "changelog": "Old release",
        "targetAbi": "10.11.11.0",
        "sourceUrl": f"https://example.invalid/{version}.zip",
        "checksum": checksum,
        "timestamp": "2026-01-01T00:00:00Z",
    }


class UpdateManifestTests(unittest.TestCase):
    def update(
        self,
        manifest: list[dict[str, object]],
        version: str,
        checksum: str = "b" * 32,
    ) -> list[dict[str, object]]:
        return update_manifest(
            manifest,
            METADATA,
            version=version,
            source_url=f"https://github.com/example/repo/releases/download/v{version}/plugin.zip",
            checksum=checksum,
            timestamp="2026-07-15T12:00:00Z",
            changelog=f"Release {version}",
        )

    def test_creates_package_and_release(self) -> None:
        result = self.update([], "1.0.0.0")

        self.assertEqual(METADATA["guid"], result[0]["guid"])
        self.assertEqual("1.0.0.0", result[0]["versions"][0]["version"])
        self.assertNotIn("targetAbi", {key: None for key in result[0] if key != "versions"})

    def test_preserves_old_versions_and_sorts_newest_first(self) -> None:
        manifest = [{**METADATA, "versions": [release("1.0.0.0")]}]

        result = self.update(manifest, "1.10.0.0")

        versions = [item["version"] for item in result[0]["versions"]]
        self.assertEqual(["1.10.0.0", "1.0.0.0"], versions)

    def test_replaces_existing_version(self) -> None:
        manifest = [{**METADATA, "versions": [release("1.0.0.0")]}]

        result = self.update(manifest, "1.0.0.0", checksum="c" * 32)

        self.assertEqual(1, len(result[0]["versions"]))
        self.assertEqual("c" * 32, result[0]["versions"][0]["checksum"])

    def test_preserves_other_packages(self) -> None:
        other = {"guid": "other", "name": "Other", "versions": []}

        result = self.update([other], "1.0.0.0")

        self.assertIs(other, result[0])
        self.assertEqual(2, len(result))

    def test_rejects_non_md5_checksum(self) -> None:
        with self.assertRaisesRegex(ValueError, "MD5"):
            self.update([], "1.0.0.0", checksum="not-an-md5")


if __name__ == "__main__":
    unittest.main()
