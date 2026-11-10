CDS TapeLab - XP kiadás 0.6.0
=====================================

Windows XP SP3 (32 bites) és újabb rendszerekre. Telepítés nélkül működik: másold át a mappát a számítógépre,
majd indítsd el a CDSTapeLab.exe fájlt.

SZÜKSÉGES A SZÁMÍTÓGÉPEN
1. Windows XP SP3.
2. Microsoft .NET Framework 4.0 (teljes változat) - "dotnetfx40_full_x86_x64.exe".
   XP-hez ez az utolsó támogatott változat.
3. ASIO illesztőprogrammal rendelkező hangkártya.
   SB Live esetén: kX Project illesztőprogram bekapcsolt ASIO-val (kX ASIO). Alternatíva: ASIO4ALL.
   Az SB Live csak 48 kHz-en dolgozik, ezért a frekvenciamenet kb. 20 kHz-ig mutatható.

ELSŐ INDÍTÁS
- Nyisd meg a Beállításokat (Settings), és válaszd ki az ASIO illesztőprogramot (kX ASIO).
  A mintavételezést hagyd automatikusan.
- Ebben a kiadásban nincs szükség licencre: a program teljes egészében működik.
- A fejállítás (azimut) mérésénél a videófelvétel (MP4) ebben a kiadásban nem érhető el.

A MÉRÉSEK
- W&F (Wow & Flutter): sebesség és hangingadozás mérése tesztkazettával (pl. 3000 vagy 3150 Hz).
- Playback frequency response: a tesztkazetta frekvenciasweepjének lejátszása; a program 1/3 oktávos sávokban
  mutatja a szintet (bal csatorna kék, jobb piros).
- Recording frequency response: felvétel + lejátszás mérése (3 fejes deck).
- Head azimuth: a lejátszófej beállítása tesztkazettával (10, 12,5 vagy 15 kHz).

HA VALAMI NEM MŰKÖDIK
Küldd el ezt a fájlt:  %AppData%\CDSTapeLab\log.txt
(A Windows Intézőben írd be a címsorba.)
A fájlban a számítógép adatai, az ASIO illesztőprogramok és a hibák szerepelnek.
