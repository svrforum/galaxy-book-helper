# PawnIO modules

Official unmodified `IntelMSR.bin` and `IntelMCHBAR.bin` from PawnIO.Modules **0.2.11**.

- Release: https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.11
- Complete matching source: https://github.com/namazso/PawnIO.Modules/tree/0.2.11
- Source archive: https://github.com/namazso/PawnIO.Modules/archive/refs/tags/0.2.11.zip
- License: LGPL-2.1-or-later; see COPYING and source file notices.
- Both entry-point source files are included in `source/`; shared compiler/includes are in the complete upstream source.
- Hardware access uses the public device IOCTL interface in https://github.com/namazso/PawnIO/blob/master/PawnIO/include/pawnio_um.h and the wire layout of PawnIOLib. No PawnIOLib code is compiled or linked into this app.
- Module hashes are pinned in `hardware/PawnDevice.cs`. Official PawnIO also verifies its module signatures. The app does not disable this verification or require the unrestricted edition.

The driver is a separate installation from https://pawnio.eu/ . The downloaded installer in `.tools` is not run automatically.
