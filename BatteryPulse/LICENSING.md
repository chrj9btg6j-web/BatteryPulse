# BatteryPulse licensing and release audit

BatteryPulse's original application source code, tests, build scripts, and installer script are offered under the [Mozilla Public License 2.0](LICENSE). Original BatteryPulse artwork is not automatically covered by this code license. Third-party files retain their respective owners' rights. The status bar now draws its own generic code icon; it no longer embeds an OpenAI mark.

The source for distributed BatteryPulse executables is available at <https://github.com/chrj9btg6j-web/BatteryPulse>. Release notes must identify the source revision used for each executable.

The installer displays the unmodified MPL-2.0 text and installs a copy of this file and the license. The four old installer license templates are no longer used.

## Third-party material to verify before a new public build

| Material | Current use | Release check |
| --- | --- | --- |
| `runtime/LibreHardwareMonitor/` | LibreHardwareMonitor 0.9.6 and bundled DLLs are copied into the installer. | All 43 packaged files matched SHA-256 hashes from the [upstream v0.9.6 ZIP](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6). Its [MPL-2.0 license](runtime/LibreHardwareMonitor/LICENSE) and [third-party notices](runtime/LibreHardwareMonitor/THIRD-PARTY-NOTICES.txt) are now included. Review the bundled dependencies' individual terms before signing. |
| `assets/installer/languages/*.isl` | Inno Setup language files compiled into the installer. | Confirm their upstream terms and attribution. The translator credits in the source files must be preserved. |
| Other `assets/branding/` images | Product and lightning artwork. | Confirm origin and rights for every image before granting an artwork license or making a signing eligibility claim. |

**Signing status:** MPL-2.0 adoption for the original code does not itself establish that the full package qualifies for SignPath Foundation. Do not request signing or replace public release assets until the checks above and the required source-linked build, policy, privacy, and review process are complete.
