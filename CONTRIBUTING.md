# Contributing

Thanks for helping build DEX Station Planner!

- **Discuss first** for larger features: open an issue describing the problem and your proposed approach.
- **Keep logic in Core.** Geometry, calculations and file formats belong in `DexStationPlanner.Core` with unit tests; the WPF app should stay a thin layer over it.
- **SI units in the model.** Convert only for display.
- **Tests:** `dotnet test` must pass, and new Core behavior needs tests.
- **Style:** follow the surrounding code (nullable enabled, file-scoped namespaces, small focused classes). Builds must be warning-free.
- **Equipment catalog:** additions to `src/DexStationPlanner.Core/Equipment/BuiltInEquipment.json` need realistic dimensions, mass and power, with a short description. Cite a source in your PR if you can.
- **Licensing:** contributions are accepted under the Apache License 2.0. Don't add dependencies with incompatible licenses, and update `NOTICE` when adding third-party components.
