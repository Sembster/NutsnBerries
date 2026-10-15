## shadcn/ui: sammanfattning

**Vad det gör och för vem.** shadcn/ui är ett öppen källkod-verktyg för att bygga ditt eget React-komponentbibliotek. Det är inte ett paket man installerar färdiga komponenter från. Ett CLI kopierar komponentens källkod in i ditt projekt, där du äger den och kan ändra den fritt. Det riktar sig till frontendutvecklare och team som vill ha tillgängliga, väldesignade byggstenar (knappar, formulär, dialoger, diagram, sidopaneler) utan att bli inlåsta i ett biblioteks API. Team kan också publicera egna register med komponenter som andra kan installera.

**Arkitektur i korthet.** Det är ett pnpm + Turborepo-monorepo med tre delar:

- **`packages/shadcn`:** CLI:t (`init`, `add`, `search`, `info`, plus en MCP-server). Det löser upp registerposter och skriver om källfiler i målprojektet.
- **`packages/registry`, `react`, `helpers`:** biblioteket för hämtning, uppslag och schema av register samt små delade paket.
- **`apps/v4`:** en Next.js-sajt som innehåller dokumentationen, komponentregistret (`registry/`), live-förhandsvisningar samt registerbygget och hälsokontrollerna.

Dessutom finns startmallar i `templates/` för Next, Vite, Astro, React Router och TanStack Start, samt en mapp med agent-`skills/`.

**Tre viktiga beroenden och varför**

1. **Radix UI (och Base UI):** de ostylade, tillgängliga primitiverna under de flesta komponenter. De ger tangentbordshantering, fokushantering och ARIA, så projektet bara behöver äga utseendet och sammansättningen.
2. **Tailwind CSS (med class-variance-authority):** all stilsättning, via semantiska designtokens och varianter. Komponenterna är vanliga utility-klasser i dina egna filer, vilket gör dem lätta att läsa och ändra utseende på.
3. **Next.js + Fumadocs:** dokumentationssajten och registervärden. MDX-innehåll, live-förhandsvisningar av komponenter och register-JSON serveras från en och samma app.

**En sak som förvånade mig i strukturen**
Svår fråga att svara på när det gäller tekniker som man inte jobbat med men en sak i allmänhet som jag blev förvånad över var att repot innehåller en .claude-folder,
vilket jag antar betyder att Claude har använts för att ta fram repot? En annan sak är att det verkar kunna användas av de fem för tillfället största FE-ramverken.