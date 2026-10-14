# Tray memory investigation

## 2026-10-14: single-file compression

The installed 0.1.9 tray was observed at approximately 71 MiB private bytes.
Releasing hidden forms alone did not address the largest source of private memory.

An isolated STA WinForms probe compiled the same tray sources and embedded icons.
It created a tray application context without changing installed startup preferences
or sending service requests, then created and painted both forms off-screen,
disposed the forms, and sampled memory. Two self-contained win-x64 single-file
publishes differed only in `EnableCompressionInSingleFile`.

Private bytes in MiB:

| Stage | Compressed, run 1 | Uncompressed, run 1 | Compressed, run 2 | Uncompressed, run 2 |
| --- | ---: | ---: | ---: | ---: |
| Startup | 46.09 | 10.45 | 46.00 | 10.29 |
| Both forms created | 68.16 | 25.79 | 61.33 | 18.40 |
| Forms disposed | 68.22 | 25.85 | 61.39 | 19.45 |
| After diagnostic GC | 67.95 | 24.61 | 60.80 | 18.31 |

The managed heap after diagnostic GC was 0.19 MiB in both variants. Compression
added about 36 MiB at startup and 42–43 MiB after the UI had been exercised.
Disabling executable compression allows bundled assemblies to remain file-backed
instead of requiring decompressed assembly data in private memory. MSI cabinet
compression is retained; installation still requires no separate .NET runtime.

These are probe results, not measurements of an upgraded installed application.
The probe does not reproduce active leases, real input-method interaction, or every
Windows UI integration. Task Manager's default memory column, total working set,
private bytes, and managed-heap size are different metrics. Compare the same metric,
scenario, and elapsed time before and after installation.

The forced GC was a diagnostic step in the external probe only. The application
does not force GC or trim the working set. A smaller managed heap alone does not
guarantee that committed pages or resident memory immediately return to Windows.
