# Feltöltés GitHubra és automatikus kiadások

Célrepo: **madarlevi/DuplexPrint**. Az előkészítés nem hozott létre repót, nem
pusholt, és nem indított publikus kiadást. A helyi projekt gyökerében jelenleg
nincs `.git` könyvtár. Az `artifacts/` alatti ellenőrző Git-munkaterületet ne töltsd fel.

## Első feltöltés üres repóba

1. GitHubon jelentkezz be `madarlevi` néven. Hozd létre a **DuplexPrint** repót,
   ha még nem létezik. Üres repónál ne generáltass új README-t, licencet vagy
   gitignore-t: ezek már készen vannak.
2. Nyiss PowerShellt a projekt gyökerében, ahol a `DuplexPrint.slnx` található.
3. Ha nincs még Git-előzményed ebben a mappában:

```powershell
git init -b main
git add .
git diff --cached --stat
git ls-files
```

Ellenőrizd a listát: ne legyen benne `artifacts`, `.vs`, `bin`, `obj`, privát
`settings.json`, PDF-dokumentum, EXE/DLL/ZIP/MSIX vagy tanúsítvány/kulcs.
A `.github` mappát viszont töltsd fel, és a `packages.lock.json` fájlokat tartsd meg.
A `printer-profiles-repo` külön repóhoz készült, itt szándékosan figyelmen kívül marad.

Ha Git nevet/e-mailt kér, a saját GitHubos nevedet és ellenőrzött vagy GitHub
által adott no-reply címedet állítsd be; személyes címet nem szükséges publikálnod.
Ezután:

```powershell
git commit -m "Prepare DuplexPrint for GitHub"
git remote add origin https://github.com/madarlevi/DuplexPrint.git
git push -u origin main
```

GitHub-bejelentkezéshez használd a Git Credential Managert vagy GitHub CLI-t;
jelszót/tokenértéket ne írj forrásfájlba vagy remote URL-be.

**Ha a GitHub-repóban már vannak commitok:** előbb klónozd egy külön mappába,
majd másold bele az előkészített forrásokat és a `.github` fájlokat, a generált
mappák nélkül. Onnan commitolj/pusholj. Ne írj felül létező `.git` mappát, és
ne használj force push-t. Meglévő helyi Gitnél előbb `git status` és
`git remote -v`; ilyenkor nincs szükség új `git init` vagy ismételt remote add lépésre.

## Mit csinál automatikusan a GitHub?

- A `.github/workflows/build.yml` branch pushra, pull requestre és kézi indításra
  restore-t, Debug/Release buildet, nyomtatás nélküli teszteket és csomagolást futtat.
- A ZIP önálló .NET runtime-mal készül. Az MSIX a virtuális nyomtató kísérleti,
  alapból aláíratlan telepítője. A csomagok SHA-256 fájlt kapnak.
- A sikeres futás **Actions → futás → Artifacts → DuplexPrint-win-x64** alatt
  tölthető le. A branch push önmagában nem hoz létre Release-t.

Az online CI ezen a gépen nem lett elindítva. A helyi hálózati korlátozás miatt
az önálló runtime-csomagok letöltése itt nem sikerült; ezt az első GitHub Actions
futásnak még igazolnia kell. A helyben készült `framework-dependent` csomaghoz
.NET 10 Desktop Runtime x64 szükséges.

## Automatikus prerelease verziócímkével

Előbb várd meg a zöld buildet, nézd át a csomagot, és végezd el a dokumentált
kézi nyomtatópróbát. Ha kiadást szeretnél létrehozni:

```powershell
git tag -a v1.2.0 -m "DuplexPrint 1.2.0"
git push origin v1.2.0
```

**A tag push publikálási művelet:** elindítja a `release.yml` workflow-t.
Ez újra lefuttatja az ellenőrzéseket, majd automatikusan **GitHub prerelease-t**
hoz létre a ZIP/MSIX/SHA-256 fájlokkal. Tagformátum: `vX.Y.Z`. Már kiadott taget
ne mozgass; javításhoz új verziót használj. Az alapverziót a `.csproj`-ban,
szkriptekben, build workflow-ban és példákban is frissítsd.

A prerelease jelölés szándékos: a valódi HP papírút és az MSIX virtuális
nyomtató teljes folyamata még kézi ellenőrzést igényel. Ezek után a GitHub
Release szerkesztésével lehet stabilnak jelölni. A workflow nem futott le az
előkészítés részeként.

## Aláírt MSIX beállítása

Az `unsigned.msix` nem kész, általánosan telepíthető végfelhasználói telepítő.
Ehhez Windows által megbízhatónak tekintett csomagaláírás kell. Az aláírási
megoldás és tanúsítvány beszerzése a te döntésed; nem hoztam létre és nem
telepítettem tanúsítványt.

A repo **Settings → Secrets and variables → Actions** felületén:

| Típus | Név | Tartalom |
| --- | --- | --- |
| Variable | `MSIX_PUBLISHER` | Az aláíró tanúsítvány teljes, pontos Subject értéke; alapból `CN=madarlevi` |
| Secret | `MSIX_CERTIFICATE_BASE64` | A PFX fájl Base64 kódolt tartalma |
| Secret | `MSIX_CERTIFICATE_PASSWORD` | A PFX jelszava |
| Variable, opcionális | `MSIX_TIMESTAMP_URL` | A tanúsítványszolgáltató időbélyegző URL-je |

Ezeket csak a jogosult saját repódban add meg. PFX-et és privát kulcsot ne
commitolj. A release job a futtató ideiglenes mappájában írja ki a tanúsítványt,
aláír, ellenőriz, majd törli az ideiglenes fájlt. A tanúsítvány tárgya és a
manifest Publisher eltérése hibát okoz; a hibás kiadás nem lesz publikálva.
Titkok nélkül az MSIX egyértelműen `unsigned` néven kerül a prerelease-be.

## Külön profilrepo

A helyi `printer-profiles-repo/` tartalmát másold egy külön mappába, és annak
README-je szerint töltsd fel egy új repóba. Javasolt név:
**madarlevi/DuplexPrint-Profiles**. Saját MIT licencet, sémát és JSON-validáló
workflow-t tartalmaz. A fő alkalmazás nem tölt le automatikusan profilokat;
a felhasználó a beállításokban importálhatja őket.
