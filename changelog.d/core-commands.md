### Added

- Calculator logic for the command bar. It works on exact decimals, so 0.1 + 0.2 gives 0.3. It handles + - * / ^, brackets, unary minus and percent, so 200 + 10% gives 220. Letters are rejected and nothing is ever run as code.
- Unit converter logic for the command bar, for queries such as "5 km to mi" or "100 °C ke °F". It covers length, mass, temperature, volume, area, speed, time, data, pressure and energy. Unit names work in English and Bahasa Melayu, and the kati and tahil are included. There is no currency.
- Result ranking for the command bar. The same query always gives the same order. Calculator and unit results come first and web search is always the last row.
- Hotkey text such as "Ctrl+Alt+Space" for settings, with a check that each hotkey has Ctrl, Alt, Shift or Win. Shift on its own works only with F1 to F24.
