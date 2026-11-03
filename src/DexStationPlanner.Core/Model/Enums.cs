namespace DexStationPlanner.Core.Model;

public enum UnitSystem { Metric, Imperial }

public enum ModuleCategory
{
    General, Berthing, Laboratory, Galley, Hygiene, Medical, Power, Mechanical,
    Storage, Workshop, Communications, Office, Recreation, Corridor,
}

public enum EquipmentCategory
{
    Berthing, Galley, Hygiene, Laboratory, Medical, Office, Power, Mechanical,
    Communications, Storage, Safety, Recreation, Workshop,
}

public enum EquipmentShape { Box, Cylinder }

public enum OpeningKind { Door, Window, Hatch, Vent }

public enum ConnectorKind { Personnel, Cargo, Utility }

public enum UtilityKind { Power, Fuel, Water, Wastewater, Heat, Data }

public enum SiteEnvironment { Polar, Desert, Mountain, Temperate }
