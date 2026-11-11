# Leverabel – Lab 3: Analys av ett verkligt fel

*Datum: 2026-11-11. Repo: NutsnBerries (fork av unrealbg/BlazorShop). Inga kodändringar har gjorts i den här delen av labben.*

## Varför vi valde det här felet

Uppgiften var att leta efter ett verkligt fel i ärendehanteringen (issue tracker) i originalrepot, [unrealbg/BlazorShop](https://github.com/unrealbg/BlazorShop), med etiketten `bug` eller `confirmed bug`.

**Vad ärendehanteringen faktiskt innehåller:**

- Repot har 50 ärenden (öppna och stängda). Etiketten `bug` finns men **inget ärende har den**, och etiketten `confirmed bug` finns inte alls. Bara tre ärenden har någon etikett, och alla tre är `enhancement`.
- Vi fick därför leta efter ärenden som *i sak* är felrapporter. Fyra kandidater gicks igenom:

| Ärende | Status | Bedömning |
|---|---|---|
| **#130** "Fail closed on incomplete Bank Transfer merchant configuration" | Stängt | Ett verkligt fel som finns kvar i vår fork |
| #134 Firefox WASM bootstrap/readiness | Öppet | Verkligt, men ett tidsberoende webbläsarfel som kräver Firefox-reproduktion. Inte rimligt att analysera inom en lab |
| #139 WASM undersöker `swagger.json` för att hitta API-adressen | Öppet, `enhancement` | Designbrist snarare än fel |
| #11 "Admin Login" | Stängt | En supportfråga, inte ett fel |

**Skäl till att vi valde #130:**

1. **Felet finns i vår egen kod.** Reservvärdet `"BG00UNCR70001512345678"` (det inbyggda IBAN som används när inställningen saknas) ligger kvar i `BlazorShop.Application/Services/Payment/BankTransferNotifier.cs:34`, dit den flyttades när vi delade upp `CheckoutOrchestrator` (Lab 2, Prompt 3). Det är inte bara ett fel i någon annans repo.
2. **Allvarlighetsgraden är hög.** Kunden kan få betalningsinstruktioner med ett kontonummer som butiken aldrig har konfigurerat, både på skärmen och i mejl. Originalprojektet har själv klassat ärendet som P1 och release-blockerare.
3. **Området är avgränsat och känt.** Felet rör en enda metod och en konfigurationssektion. Vi har sedan Lab 2 karakteriseringstester för kassaflödet, så teorier går att pröva i kod.
4. **Orsaken är inte given.** Ärendet beskriver *vad* som ska ändras, men inte *varför* felet uppstod. Det passar en uppgift om att formulera och rangordna hypoteser om rotorsaken.

## Steg 1

### Uppgiften (prompten)

> Analysera det här felet och formulera tre hypoteser om rotorsaken, ordnade efter sannolikhet. För varje hypotes: förklara mekanismen och ange konkreta verifieringssteg.

### Felet i korthet

**Vad ärende #130 beskriver.** När butikens bankuppgifter saknas eller är ogiltiga ska Bank Transfer döljas som betalsätt, och direkta försök att lägga en ny bankbetalning ska avvisas innan någon order eller lagerreservation skapas. Ingen påhittad mottagare får returneras eller mejlas.

**Vad vår kod gör idag** (observerat vid läsning av koden, ännu inte körts):

- `BankTransferNotifier.BuildInfo` använder `"BG00UNCR70001512345678"` som IBAN när `BankTransfer:Iban` är tom. För `Beneficiary` (mottagare) och `BankName` (bank) finns **ingen** reserv. Med helt tom konfiguration blir resultatet alltså ett IBAN utan mottagare och bank.
- Samma värde ligger som exempelkonfiguration i `appsettings.json:45` och `appsettings.Development.json:9`, tillsammans med `"NutsnBerries Ltd."` och `"Unicredit Bulbank"`.
- `PaymentMethodService.GetPaymentMethodsAsync` filtrerar bara bort PayPal och (när Stripe är avstängt) kortbetalning. Bank Transfer visas alltid.
- Kassaflödet (`CheckoutOrchestrator.ExecuteOwnedCheckoutAsync`) har ingen kontroll av bankuppgifterna före order, lagerreservation och mejl.
- Vår egen startvalidering `BankTransferSettingsValidator` (Lab 2, Prompt 1) godkänner en helt tom sektion och godkänner exempelvärdena. Två av våra egna tester fastställer det beteendet: `BankTransfer_EmptySectionIsAllowed` (`StartupValidationTests.cs:114-118`) och `BankTransfer_ShippedSampleValuesPass` (`:120-132`).

**Konsekvens.** En kund kan få veta att betalningen ska gå till ett konto som butiken aldrig har valt. Enligt kommentaren vid vår validator klarar exempel-IBAN:en inte kontrollsiffran (mod-97), så en bank skulle sannolikt avvisa en överföring till just det kontot. Felet visar sig då som en uteblivd betalning och en förvirrad kund. Med en annan hårdkodad IBAN som råkar vara giltig skulle pengarna i värsta fall gå till fel mottagare.

### Tre hypoteser om rotorsaken

Hypoteserna är ordnade efter hur väl de förklarar felet och hur mycket stöd de har i koden hittills. De utesluter inte varandra. Hypotes 1 förklarar *varför reservvärdet finns*, hypotes 2 *varför felet nås i verkligheten även utan reservvärdet* och hypotes 3 *varför ingenting stoppade det*.

#### Hypotes 1 (mest sannolik): "Ej konfigurerat" är inget tillstånd som koden känner till, och reservvärdet är en demobekvämlighet i koden

**Mekanism.** Koden har aldrig modellerat att bankuppgifter kan saknas. I stället ersätter den en saknad IBAN med ett inbyggt värde på användningsstället (`string.IsNullOrWhiteSpace(Iban) ? "BG00…" : Iban`), så att demon ska fungera direkt utan konfiguration. Ett saknat värde döljs därmed i stället för att upptäckas, och hela kedjan efter det (skärmen, mejlet, den lagrade kvittensen) behandlar det som om det vore äkta.

**Stöd i det vi sett hittills:**

- Reservvärdet skrevs i **samma commit som bankbetalningen infördes** (`91818da`, 2025-08-20, "Add bank transfer and PayPal payment processing") och är identiskt med exempelvärdet i konfigurationen som lades till i samma commit. Det är samma demovärde på två ställen.
- Bara IBAN har en reserv, inte mottagare eller bank. Det tyder på ett quick fix för demon, inte en genomtänkt regel. Resultatet kan bli ett IBAN utan mottagare.
- Värdet har följt med genom två omskrivningar (`6d1896e` / #101 och vår uppdelning i Lab 2) utan att någon ifrågasatte det.

**Verifieringssteg:**

| # | Steg | Förväntat om hypotesen stämmer | Status |
|---|---|---|---|
| 1.1 | `git log upstream/master -S"BG00UNCR70001512345678"` för att hitta var värdet kom in | Första träffen är den commit som införde bankbetalning | **Utfört:** `91818da`, `6d1896e`, `57dbd29` |
| 1.2 | `grep -rn "BG00UNCR70001512345678"` i hela repot (utom bygg-mappar) | Värdet finns i kod, i konfiguration och i ett test, ingenstans med förklaring | **Utfört:** `BankTransferNotifier.cs:34`, `appsettings.json:45`, `appsettings.Development.json:9`, `StartupValidationTests.cs:125` |
| 1.3 | Skriv ett nytt enhetstest: `BankTransferSettings` helt tom, bankbetalning genomförs | Svaret innehåller `Iban == "BG00UNCR70001512345678"`, `Beneficiary` och `BankName` är tomma, och mejltexten innehåller samma värden | Ej utfört |
| 1.4 | Mutationstest som i Lab 2: ta bort reservvärdet tillfälligt och kör `dotnet test` | Bara tester som bygger orkestreraren med tomma inställningar går sönder. Ett sådant bygge finns i `InventoryReservationPostgreSqlTests.cs:238`, men det ligger i ett Stripe-test (`PaymentInitializationFailure_ReleasesReservationAndRestoresStock`), och bankbetalningstestet vid rad 300 använder i stället en hjälpmetod med ifyllda inställningar (`BG00TEST`). Vid läsning verkar alltså inget befintligt test bero på reservvärdet | Ej utfört. Själva mutationskörningen återstår (läsningen ovan rättar ett tidigare osäkert antagande) |
| 1.5 | Läs de två ställen där `BuildInfo` anropas (`ExecuteOwnedCheckoutAsync` och `ResumeCommittedOrderAsync`) och kontrollera att inget av dem kontrollerar inställningarna först | Ingen kontroll finns | **Utfört** vid läsning: ingen kontroll |

**Vad skulle motbevisa hypotesen:** att commit `91818da` visar att reservvärdet infördes som skydd mot något annat än saknad konfiguration (steg 1.1 pekar mot demobruk), eller att steg 1.3 visar att `Iban` aldrig kan vara tom i praktiken.

#### Hypotes 2 (sannolik): Den medföljande exempelkonfigurationen döljer att konfigurationen saknas, och produktionsflödet har ingen väg att ersätta den

**Mekanism.** Det sanna värdet "inget är konfigurerat" uppstår aldrig i vanlig drift, eftersom `appsettings.json` redan innehåller ett välformat exempelkonto. Konfigurationen byggs i lager (appsettings, sedan miljövariabler, sedan user-secrets), och ett lager som inte sätter något lämnar exempelvärdet kvar. Utan ytterligare steg körs en container i produktion alltså med `NutsnBerries Ltd.`, `Unicredit Bulbank` och exempel-IBAN:en, och reservvärdet i koden behövs inte ens. Felet träffar kunden *även om hypotes 1 åtgärdas*.

**Stöd i det vi sett hittills:**

- `compose.production.yml` skickar vidare variabler för JWT, Stripe och e-post, men **ingen för `BankTransfer__…`** (sökning i alla compose-, yml-, env- och md-filer gav bara en rad i `README.md:147` som nämner nycklarna). `.env.example` innehåller inga bankvariabler.
- Det finns ingen `appsettings.Production.json`. Produktion använder alltså `appsettings.json` med exempelvärdena.
- Startvalideringen som skyddar JWT-nyckeln mot platshållare hittar bara värden skrivna som `<...>` (`ConfigValueRules.IsPlaceholder`). Exempelkontot är skrivet som riktiga värden och passerar, vilket vårt test `BankTransfer_ShippedSampleValuesPass` bekräftar.

**Verifieringssteg:**

| # | Steg | Förväntat om hypotesen stämmer | Status |
|---|---|---|---|
| 2.1 | Sök efter `bank` (utan skiftlägeskänslighet) i `compose.production.yml`, `compose.vps.yml`, `.env.example` och runbooken | Ingen variabel som mappar till `BankTransfer__*` | **Utfört** för alla yml/env/md-filer: ingen träff utöver README.md:147 |
| 2.2 | Lista konfigurationsfilerna i API-projektet | Ingen produktionsspecifik fil | **Utfört:** bara `appsettings.json` och `appsettings.Development.json` |
| 2.3 | `docker compose -f compose.production.yml config` med de obligatoriska variablerna ifyllda, och läs `environment` för API-tjänsten | Inga `BankTransfer__`-nycklar | Ej utfört |
| 2.4 | Bygg API-avbilden och läs `/app/appsettings.json` i containern (eller starta API:t med `ASPNETCORE_ENVIRONMENT=Production`) och fråga efter den bundna konfigurationen | Exempelvärdena är aktiva | Ej utfört. Avbilden är cirka 1 GB att hämta, så vi frågar först |
| 2.5 | Nytt test: bind `BankTransferSettings` från den verkliga `appsettings.json` och kör `BankTransferSettingsValidator` | `Succeeded`, och värdena är exempelvärdena | Delvis täckt av `BankTransfer_ShippedSampleValuesPass`, som dock använder handskrivna värden och inte själva filen |

**Vad skulle motbevisa hypotesen:** att produktionsflödet i själva verket skriver över bankuppgifterna på ett sätt vi inte hittat (till exempel en monterad konfigurationsfil i runbooken eller i `compose.vps.yml`). Steg 2.1 och 2.3 prövar det.

#### Hypotes 3 (möjlig, bidragande orsak): Ingen enskild definition av "konfigurerad", så alla tre skydden saknas eller säger emot varandra

**Mekanism.** Tre ställen borde bevaka bankuppgifterna: (a) vid start (validering), (b) när betalsätten listas (upptäckt) och (c) när en bankbetalning läggs (exekvering). Vart och ett skulle behöva samma regel om vad "komplett och användbar" betyder. I stället finns ingen regel i upptäckten och ingen i exekveringen, och startvalideringen (som vi själva skrev) tillåter uttryckligen att allt är tomt ("metoden är helt enkelt inte konfigurerad"). Den meningen är aldrig genomförd någon annanstans: ingen del av systemet gör att en ej konfigurerad metod faktiskt försvinner. Därför kan ett tomt tillstånd passera start och sedan nå kunden som ett påhittat IBAN.

Det förvärras av att kvittensen sparas: kassaflödet bygger bankinstruktionerna och lagrar dem som en förberäknad framgångskvittens (`SetPendingOutcomeAsync`) **innan** lagerreservationen görs, och en upprepad förfrågan med samma `Idempotency-Key` svarar med den lagrade kvittensen. Ett felaktigt IBAN som en gång har lagrats upprepas alltså även efter att konfigurationen rättats.

**Stöd i det vi sett hittills:**

- Läsning av `BankTransferSettingsValidator`: tre tomma fält ger `Succeeded` (`BankTransfer_EmptySectionIsAllowed`).
- Läsning av `PaymentMethodService`: Bank Transfer filtreras inte på konfiguration.
- Läsning av `ExecuteOwnedCheckoutAsync`: ordningen är bygg kvittens, spara kvittens, skapa order med lagerreservation.

**Verifieringssteg:**

| # | Steg | Förväntat om hypotesen stämmer | Status |
|---|---|---|---|
| 3.1 | Läs validatorn och dess test | Tom sektion godkänns | **Utfört** |
| 3.2 | Läs `PaymentMethodService` | Ingen koppling till bankinställningar | **Utfört** |
| 3.3 | Starta API:t med `BankTransfer__Iban`, `BankTransfer__Beneficiary` och `BankTransfer__BankName` satta till tomma värden och anropa `GET /api/Payment/methods` | API:t startar, och Bank Transfer finns med i listan | Ej utfört. Att lägga en order kräver en inloggad kund, så den delen prövas som test mot orkestreraren i stället |
| 3.4 | Kontrollera hur .NET binder en tom miljövariabel (`Environment.SetEnvironmentVariable("BankTransfer__Iban", "")` med `AddEnvironmentVariables()`) | Värdet blir tomt, inte hoppat över, så att det skriver över exempelvärdet i `appsettings.json` | Ej utfört |
| 3.5 | PostgreSQL-test: lägg en bankbetalning med tomma inställningar, ändra sedan inställningarna och upprepa samma `Idempotency-Key` | Den andra förfrågan returnerar fortfarande reservvärdet | Ej utfört |
| 3.6 | Sök i testerna efter ett som kräver att en tom konfiguration *avvisas* | Inget sådant test finns | **Utfört** (sökning på `BankTransferSettings`): inget test avvisar tom konfiguration. Två tester fastställer motsatsen |

**Vad skulle motbevisa hypotesen:** att Bank Transfer i verkligheten döljs på något sätt vi missat (till exempel på klientsidan), eller att lagrade kvittenser byggs om vid återspelning.

### Hur hypoteserna hänger ihop

| | Förklarar | Om bara denna åtgärdas |
|---|---|---|
| Hypotes 1 | Varför ett påhittat IBAN skapas när värdet saknas | Reservvärdet försvinner, men exempelkontot i konfigurationen kvarstår (hypotes 2) |
| Hypotes 2 | Varför kunden får ett exempelkonto i en vanlig produktionsdrift | Konfigurationen blir tom, men koden fyller i reservvärdet (hypotes 1) |
| Hypotes 3 | Varför ingenting upptäckte något av de två | Felet upptäcks vid start, men hypotes 1 och 2 finns kvar bakom det |

Ärende #130 föreskriver en lösning som täcker alla tre (ta bort reservvärdet, validera på ett gemensamt sätt, dölja metoden och avvisa nya försök), men det är en slutsats att pröva i nästa steg och inte något som den här analysen utgår från.

### Vad som är gjort och inte gjort

- **Gjort:** läsning av koden och konfigurationen, `git log -S` över originalets historik, sökning i compose- och env-filer, läsning av ärende #130 och de berörda testerna.
- **Inte gjort:** inget kördes. Verifieringsstegen markerade "Ej utfört" återstår, och ingen hypotes är därför bekräftad, bara stödd av observerad kod. Inga kodändringar har gjorts.
- **Begränsning:** "förväntat resultat" är vår förutsägelse utifrån läsning, inte mätning.

## Steg 2

### Uppgiften

> Utifrån hypotes 1: vilka filer och funktioner ska jag undersöka? Vad ska jag leta efter?

**Hypotes 1 i en mening:** "ej konfigurerat" är inget tillstånd som koden känner till, och det inbyggda IBAN:et är en demobekvämlighet som ligger på användningsstället. Undersökningen ska därför svara på tre frågor: *var uppstår värdet, vem använder det, och finns det något som skulle ha stoppat det?*

### Undersökningsordning

Gå igenom filerna i den här ordningen. Den följer värdets väg från konfiguration till kund. Radnumren gäller commit `2c7a06d` och söks enklast fram med namnet på funktionen.

| # | Fil och funktion | Vad du ska leta efter | Redan observerat (bekräfta själv) |
|---|---|---|---|
| 1 | `BlazorShop.Application/DTOs/Payment/BankTransferSettings.cs` | Typerna på `Iban`, `Beneficiary` och `BankName`. Är de nullbara eller `required`, eller har de standardvärdet `string.Empty`? | Alla tre är `string = string.Empty`. Typen säger alltså "alltid ett värde", och ett saknat värde ser därmed ut som ett giltigt tomt värde |
| 2 | `BlazorShop.Infrastructure/DependencyInjection.cs:191` | Hur sektionen `BankTransfer` binds till klassen. Finns `ValidateOnStart`, `Validate` eller någon annan kontroll direkt vid bindningen? | Bara `Configure<BankTransferSettings>(...)`, ingen kontroll. Vår validering läggs på från ett annat håll (`NutsnBerriesHardeningExtensions.cs:36`) |
| 3 | `BlazorShop.Application/Services/Payment/BankTransferNotifier.cs`, `BuildInfo` (rad 31–42) | Raderna som tar ett värde ur inställningarna. Vilka fält har en reserv och vilka inte? Förklaras reserven med en kommentar? | Bara `Iban` (rad 33–35) har en reserv, ingen kommentar. `Beneficiary` (36), `BankName` (37) och `AdditionalInfo` (41) kopieras rakt av |
| 4 | Samma fil, `TrySendEmailAsync` (rad 44–70) | Vilken information mejlet bygger på, och om något kontrollerar den innan den skickas | Mejlet använder det som `BuildInfo` gav, inklusive reservvärdet. Det enda som kontrolleras är kundens e-postadress (rad 48). `catch`-blocket (rad 66) loggar och sväljer fel |
| 5 | `BlazorShop.Application/Services/Payment/CheckoutOrchestrator.cs`, `ExecuteOwnedCheckoutAsync` (rad 131–241) | Anropet till `BuildInfo` (rad 201), ordningen mellan det, `SetPendingOutcomeAsync` (rad 206) och `CreateOrderWithInventoryAsync` (rad 218), och om något mellan start och `BuildInfo` tittar på inställningarna | Ingen kontroll av inställningarna. Informationen byggs och sparas som kvittens *före* lagerreservationen |
| 6 | Samma fil, `ResumeCommittedOrderAsync` (rad 244–275) och `CompleteLocalPaymentAsync` (rad 277–300) | Den andra vägen till samma värde (`BuildInfo` på rad 268, mejlet på rad 295–298) | Samma anrop utan kontroll, så att reservvärdet nås från två håll |
| 7 | `BlazorShop.Web/Pages/Payments/Cart.razor` (rad 176–200) och `Cart.razor.cs` (rad 291–295) | Vad kunden ser, och om något döljer fält som är tomma | Banknamn, mottagare och IBAN visas utan villkor. Bara `AdditionalInfo` är skyddad med `IsNullOrWhiteSpace`. Enda villkoret i `.razor.cs` är `result.BankTransfer is not null` |
| 8 | Historiken: `git show 91818da`, sök efter `BG00UNCR`; `git show 6d1896e`; `git log upstream/master -S"BG00UNCR70001512345678"` | Commit-meddelandet och diffen där värdet infördes: står det varför, och infördes det tillsammans med exempelkonfigurationen? Vad hände med det när koden skrevs om? | Infördes i `91818da` (2025-08-20) i mejlkoden, tillsammans med samma värde i `appsettings.json`. Flyttades och ändrades i `6d1896e` (#101) |
| 9 | Testerna: `CheckoutOrchestratorTests.cs`, `CheckoutOrchestratorCharacterizationTests.cs`, `InventoryReservationPostgreSqlTests.cs` och `StartupValidationTests.cs` | Finns ett test som *kräver* reservvärdet, och finns ett test som kräver att tom konfiguration avvisas? Sök på `new BankTransferSettings()` och på `BG00` | Inget test verkar kräva reservvärdet (det tomma bygget på rad 238 ligger i ett Stripe-test). Inget test kräver heller att tom konfiguration avvisas. Två tester kräver tvärtom att den *godkänns* |

### Vad du ska leta efter

**Tecken som stöder hypotes 1:**

1. Reserven finns på *ett* ställe, vid användningen, och ingenstans i validering, betalsättslista eller kassaflödets början (steg 2, 3, 5).
2. Reserven är inkonsekvent: bara IBAN har en, vilket ger ett IBAN utan mottagare (steg 3).
3. Värdet är kopierat från exempelkonfigurationen och infördes i samma commit som funktionen (steg 8).
4. Ingen kommentar, dokumentation eller test förklarar reserven som ett avsiktligt beteende (steg 3, 8, 9).
5. Både skärmen och mejlet återger värdet utan kontroll, så felet sprids i stället för att stoppas (steg 4, 6, 7).

**Tecken som talar emot hypotes 1:**

1. En kontroll *före* `BuildInfo` som gör att tom `Iban` aldrig kan nå koden (steg 2, 5).
2. Ett commit-meddelande, en README-rad eller en runbook som uttryckligen säger att reserven är en avsedd demofunktion eller ett skydd mot något annat (steg 8).
3. Ett test eller ett dokument som kräver reserven (steg 9). Då är den ett avsiktligt beteende och inte en miss.
4. Gränssnittet döljer instruktionerna när fälten är tomma (steg 7). Då når reservvärdet inte kunden på skärmen, bara i mejlet.

### Beslutsregel

- **Hypotes 1 stämmer** om punkt 1–3 bland stödtecknen håller och ingen av de motsägande punkterna hittas.
- **Hypotes 1 försvagas** om du hittar en kontroll före `BuildInfo` eller ett avsiktligt beteende. Då flyttas tyngdpunkten till hypotes 2 (exempelkonfigurationen), som inte beror på reservvärdet.
- **Skriv ner varje fynd med fil och rad**, och skilj på *läst i koden* och *bekräftat genom körning*. Läsning ger stöd, bara en körning ger bevis.

### Frivillig bekräftelse genom körning

Om du vill gå från läsning till bevis efter genomgången, i ordning efter minst ansträngning:

1. **Mutationstest:** ta bort raderna 33–35 i `BankTransferNotifier.cs` tillfälligt (låt `Iban` kopieras rakt av), kör `dotnet test BlazorShop.Tests/BlazorShop.Tests.csproj -c Release` och se vilka tester som går sönder. Förväntat vid läsning: inga, vilket skulle bekräfta att inget test beror på reservvärdet. Återställ sedan filen.
2. **Nytt test:** `BankTransferSettings` helt tom, en bankbetalning genomförs. Förväntat: `Iban` är `BG00UNCR70001512345678`, `Beneficiary` och `BankName` är tomma, och mejltexten innehåller samma värden.
3. **Git:** `git show 91818da --stat` och sök i diffen efter `BG00UNCR` för att läsa den ursprungliga koden i sitt sammanhang.

*Ingenting av detta är utfört i Steg 2. Kolumnen "Redan observerat" kommer från vår läsning av koden, inte från körning.*
