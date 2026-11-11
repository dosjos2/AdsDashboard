# Verifikasjon

## Board og flytting – 3. november 2026

79 klient-/modellkontroller bestått, én tidligere kjent DPAPI-kontroll hoppet over. De 29 nye board-kontrollene kjører den faktiske klienten med HTTP mot Postman. De dekker team- og WIQL-sideinndeling, batcher på maksimalt 200, områder med/uten underområder, backlog-iterasjon, dynamiske kolonnefelt, delte kolonner, statuskoblinger, tvetydig plassering, søk og egne oppgaver. Skrivekontrollene dekker revisjonstest, ny kolonne med samme workflow-status, nullstilling av Done-flagget, bevart svømmebane og bekreftelse ved etterfølgende lesing.

Feilscenarioene omfatter 403, workflow-regler, 429, konkurrerende endring, mistet svar og mangelfull bekreftelse. I tilfellene med mistet eller mangelfullt svar lagret mocken faktisk endringen først. Klienten sendte nøyaktig ett PATCH-kall, rapporterte ukjent resultat, og en ny lesing viste den lagrede endringen.

Ni ytterligere WPF-kontroller kjører selve board-kontrollen mot samme HTTP-simulering:

```powershell
# Etter vanlig bygg, med Postman-simuleringen på port 4010:
bin/Release/net10.0-windows/DevOpsPulse.exe --board-checks
```

Resultatet finnes i `bin/Release/net10.0-windows/board-ui-checks.txt`. UI-kontrollene verifiserer at avvist flytting beholder kortets opprinnelige kolonne, viser rettighetsveiledning, sperrer gjentatt forsøk før ny lesing, oppdager faktisk lagring etter mistet svar, viser bekreftet flytting og bevarer en annens endring. Kontobytte rydder gamle kort og valg. Den vanlige `--preview`-kontrollen viser `board.png` og trykker på den faktiske Flytt-knappen i demo.

Board-kontrakten ligger i `postman/specs/board.yaml`. Etter regenerering av samlingen kjøres `node scripts/Prepare-BoardChecks.cjs`. Samlingen er eksplisitt begrenset til den lokale, syntetiske simuleringen fordi den inneholder én skriveforespørsel. Null skrivekall er gjort mot Helsedir.

```powershell
postman spec lint postman/specs/board.yaml --issueType=syntax
postman collection lint postman/collections/board
postman collection run postman/collections/board --no-report-events
```

Faktisk Postman-resultat (syntetisk server):

```text
-------------------------------------------------------------------
|                         |          executed |            failed |
--------------------------+-------------------+--------------------
|              iterations |                 1 |                 0 |
--------------------------+-------------------+--------------------
|                requests |                 6 |                 0 |
--------------------------+-------------------+--------------------
|            test-scripts |                 6 |                 0 |
--------------------------+-------------------+--------------------
|      prerequest-scripts |                 0 |                 0 |
--------------------------+-------------------+--------------------
|              assertions |                18 |                 0 |
-------------------------------------------------------------------
```

Primærkilder: [Boards Get](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/boards/get?view=azure-devops-rest-7.1), [Teams Get Teams](https://learn.microsoft.com/en-us/rest/api/azure/devops/core/teams/get-teams?view=azure-devops-rest-7.1), [Team Field Values](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/teamfieldvalues/get?view=azure-devops-rest-7.1), [Team Settings](https://learn.microsoft.com/en-us/rest/api/azure/devops/work/teamsettings/get?view=azure-devops-rest-7.1) og [Work Items Update](https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/work-items/update?view=azure-devops-rest-7.1). Testene etablerer oppførsel mot de modellerte svarene. Helsedirs faktiske board-oppsett, tokenrettigheter og workflow-regler gjenstår å verifisere med brukerens tilkobling.

Verifisert 2.–3. november 2026 på Windows med .NET SDK 10.0.401 og Postman CLI 1.83.1.

## Lokal API-simulering

Mocken er skrevet fra Microsofts dokumenterte REST 7.1-kontrakt og de faktiske klientkallene, med fiktive prosjekter og identiteter. Postman var ikke innlogget, så søk etter en publisert mock fra eieren ble utelatt. Orbit kunne ikke nås fra arbeidsmiljøet; Microsoft Learn ble brukt som primærkilde. Ingen artefakter er publisert.

Start simuleringen i en terminal:

```powershell
postman simulation run postman/simulations/devops.sim.yaml --port 4010 --no-history --output ndjson
```

Kjør klienttestene i en annen terminal:

```powershell
dotnet restore tests/IntegrationTests.csproj --configfile NuGet.Config
dotnet run --project tests/IntegrationTests.csproj --no-restore
```

Testene kjører den faktiske AzureDevOpsClient med ekte HTTP mot Postman på loopback, samt visningsmodellens filtre. De dekker prosjektsider, 201 oppgavereferanser i batcher på maks 200, utelatte oppgaver, PR-sider, deduplisering av roller, historikk, reviewers og utkast, byggsider og siste kjøring per definisjon, delvis 403, 401, tvetydig identitet, HTML i stedet for JSON, 429 med Retry-After og avbrudd. UI-forhåndsvisningen kjører navigasjon og sjekker at prosjektvalget beholdes etter søk og oppdatering.

Resultat etter endringen 3. november: 35 klient-/filterkontroller bestått. Fire PBI-kontroller verifiserer uthenting, nøkkeltall/fordeling, typefilter og visning av fullførte Product Backlog Items. De seks nye kontrollene verifiserer beskrivelse, avsnitt og lister, metadata, akseptansekriterier, bug-steg, manglende valgfrie felter og tekstkonvertering av HTML. Én DPAPI-test ble hoppet over: sandbox-prosessen fikk Win32-feil 2 ved kryptering med gjeldende brukerprofil. Testen forsøker en rundtur med en syntetisk hemmelighet uten å lagre legitimasjon. Kryptert tokenlagring må derfor verifiseres fra normal Windows-oppstart utenfor sandboxen. Appen rapporterer eventuell lagringsfeil og fortsetter med token i minnet; den lagrer aldri tokenet i klartekst som reserve.

WPF-forhåndsvisningen klikker på den faktiske oppgaveknappen, kontrollerer at riktig oppgave åpnes i et eget ikke-modalt vindu, åpner en annen oppgave samtidig og sjekker at lukking bare påvirker det valgte vinduet. Manglende felter håndteres med tomtekst eller skjulte seksjoner. `work-item.png` viser detaljvinduet. Ingen nettleser åpnes fra oppgaveknappen. Feltene følger allerede eksisterende batch-kall, uten nye API-endepunkter. Offisiell [feltreferanse](https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/fields/list?view=azure-devops-rest-7.1) ble kontrollert.

Hvis sandboxen nekter tilgang til brukerens konfigurasjonsmapper, bruk prosjektlokal profil **i testterminalene**:

```powershell
$env:APPDATA = Join-Path (Get-Location) '.buildprofile'
$env:USERPROFILE = Join-Path (Get-Location) '.buildprofile'
```

Dette er bare for bygg-/testverktøy. Desktopappen skal startes normalt for å lagre innstillinger på riktig Windows-bruker.

## Kontraktkontroller

Oppdatering for diskusjon og varsler 3. november: 50 klient-/filterkontroller bestått, én DPAPI-kontroll fortsatt hoppet over. Kommentar-API-et er testet med flere sider, duplikater, slettede kommentarer, markdown, metadata, tom liste, 403, ugyldig JSON-kontrakt, gjentatt token og feil på neste side. Varsling er testet for første stille utgangspunkt, nye tildelinger, nye tekst-/kommentaromtaler, eksisterende omtale ved annen endring, deduplisering, ulest-teller og kontobytte. Testene kjører klienten gjennom faktisk HTTP mot den lokale Postman-simuleringen. Ingen live Helsedir-varsling er verifisert.

WPF-forhåndsvisningen tester at nettknappen er aktiv på første åpning av en ekte oppgave før kommentarkallet fullføres, at diskusjonen vises etterpå, at feilmelding kan prøves på nytt, at tom diskusjon er tydelig, og at lukking avbryter ventende henting. Innboksen testes med en syntetisk hendelse. Bilder finnes som `discussion.png` og `notifications.png` ved siden av de øvrige forhåndsvisningene.

Nyeste collection run: 7 requests, 21 assertions, 0 failures. OpenAPI-syntaks og de 7 forespørselsfilene passerer lint. Offisielle kilder: [Get Comments](https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/comments/get-comments?view=azure-devops-rest-7.1) og [Work Items Batch](https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/work-items/get-work-items-batch?view=azure-devops-rest-7.1). Omtalesøk bruker endrede oppgaver og tidsbestemte snapshots; webmakroen `@RecentMentions` brukes ikke i REST-kall.

Prosjektfiltrene kan testes uten API-simulering:

```powershell
dotnet run --project tests/IntegrationTests.csproj --no-restore -- --filters-only
```

12 kontroller bestått: HESA/Blåresept som standard for pipelines, felles prosjektgruppe for oppgaver/PR-er/bygg, separate pipelinevalg, kombinerte filtre, store/små bokstaver, ASCII-navn, oppdatering og manglende prosjekter. WPF-forhåndsvisningen kontrollerer også at gruppevalget og det separate pipelinevalget beholdes etter oppdatering.

```powershell
postman spec lint postman/specs/azure-devops.yaml --issueType=syntax
postman collection lint postman/collections/azure-devops
postman environment lint postman/environments
postman collection run postman/collections/azure-devops -e postman/environments/simulation.environment.yaml --no-report-events
```

Resultat: OpenAPI uten syntaksfeil, seks forespørselsfiler og ett miljø uten lintfeil eller advarsler. Governance lint krever Postman-innlogging og ble ikke kjørt. Utdrag fra faktisk collection run:

```text
-------------------------------------------------------------------
|                         |          executed |            failed |
--------------------------+-------------------+--------------------
|              iterations |                 1 |                 0 |
--------------------------+-------------------+--------------------
|                requests |                 6 |                 0 |
--------------------------+-------------------+--------------------
|            test-scripts |                 6 |                 0 |
--------------------------+-------------------+--------------------
|      prerequest-scripts |                 0 |                 0 |
--------------------------+-------------------+--------------------
|              assertions |                18 |                 0 |
-------------------------------------------------------------------
```

Samlingen ble generert fra spesifikasjonen og gjort kjørbar med `node scripts/Prepare-ContractChecks.cjs`. Den kan regenereres med `postman spec generate collection postman/specs/azure-devops.yaml -n azure-devops --force` etterfulgt av skriptet. Simuleringsmiljøets `test-token` er offentlig, syntetisk testdata, og gir ingen DevOps-tilgang.

## Hva kontrollene fastslår

Bygg og lokal simulering verifiserer klientens håndtering av de modellerte kontraktene og feiltilfellene. Forespørslene ble observert i Postman-simuleringens request-logg. Ingen live Helsedir-data, reelle brukertilganger, organisasjonens PAT-policy eller prosesspesifikke felter/tilstander er verifisert. Dette gjenstår ved første tilkobling i appen. Tilgangsfeil og ufullstendige prosjektdata presenteres i appen.
