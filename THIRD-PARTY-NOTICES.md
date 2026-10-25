# Third-party notices

Atap is MIT licensed. It ships with the packages below. Each keeps its own licence. The versions are pinned in the `packages.lock.json` files.

Every package is part of every build. Each one loads only when the feature that needs it is turned on.

| Package | Version | Licence | Copyright | Used for |
| --- | --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | Copyright (c) .NET Foundation and Contributors | View models and commands |
| Velopack | 1.2.161 | MIT | Copyright © Velopack Ltd. All rights reserved. | Installs and updates. Not in Store builds. |
| NAudio.Wasapi | 3.3.0 | MIT | © Mark Heath 2026 | Volume, audio devices and per-app volume |
| NAudio.Core | 3.3.0 | MIT | © Mark Heath 2026 | Used by NAudio.Wasapi |
| System.Numerics.Tensors | 9.0.0 | MIT | © Microsoft Corporation. All rights reserved. | Used by NAudio.Core |
| MQTTnet | 5.2.0.1603 | MIT | Copyright (c) .NET Foundation and Contributors | Home Assistant over MQTT |
| System.Management | 10.0.13 | MIT | © Microsoft Corporation. All rights reserved. | Brightness change events |

The .NET runtime and the Windows Desktop runtime ship inside Atap. They are MIT licensed. Copyright (c) .NET Foundation and Contributors.

The `atap` tool is built with the .NET NativeAOT compiler (Microsoft.DotNet.ILCompiler 10.0.13, MIT, Copyright (c) .NET Foundation and Contributors). It links parts of the .NET runtime into `atap.exe`, so the runtime notices below apply to it too.

Build-only tools, not shipped: MinVer (Apache-2.0) for version numbers.

## Notice files

Some packages bring their own list of the code they include from others. Those lists ship unchanged in the `licenses` folder next to Atap:

- `licenses/CommunityToolkit.Mvvm-notices.txt` comes with CommunityToolkit.Mvvm 8.4.0.
- `licenses/dotnet-runtime-10.0-notices.txt` comes with System.Management 10.0.13 and Microsoft.DotNet.ILCompiler 10.0.13. The .NET 10.0.13 runtime has the same file.
- `licenses/dotnet-runtime-9.0-notices.txt` comes with System.Numerics.Tensors 9.0.0.

Atap's own licence ships as `LICENSE.txt`.

## MIT License

Every package in the table uses this licence. Its copyright line is in the table.

```text
MIT License

Copyright (c) <the copyright line from the table>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
