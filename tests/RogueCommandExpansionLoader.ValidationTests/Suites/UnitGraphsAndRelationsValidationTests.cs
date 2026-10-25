#nullable enable
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;
using RogueCommandExpansionLoader.Compatibility;
using RogueCommandExpansionLoader.Content;
using RogueCommandExpansionLoader.Contracts;
using RogueCommandExpansionLoader.Modules;
using RogueCommandExpansionLoader.Runtime;
using RogueCommandExpansionLoader.Runtime.Visual;

internal static partial class ValidationTestRunner
{
    internal static IEnumerable<(string Name, Action Run)> GetUnitGraphsAndRelationsValidationTests() => new (string Name, Action Run)[]
    {
    ("unit upgrade rejects a production factory target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = pack.units[0].productionBuilding.id,
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.target-unknown");
        });
    }),
    ("unit upgrade accepts a registered native target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = "NativeWalker",
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectValid(pack, root, new SnapshotWithUnits(["NativeWalker"]), new KnownModuleValidator());
        });
    }),
    ("unit upgrade accepts a unit declared by another pack", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition
                {
                    targetUnitId = "other:registered-unit",
                    requiredExperienceLevel = 1,
                    cost = 10
                }
            ];
            ExpectValid(pack, root,
                new SnapshotWithUnits(["other:registered-unit"]), new KnownModuleValidator());
        });
    }),
    ("unit synergy rejects a production factory target", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies =
            [
                new ContentUnitSynergyDefinition
                {
                    id = "sample:test-synergy-factory",
                    statName = "damage",
                    multiplier = 1,
                    radius = 1,
                    targetUnitIds = [pack.units[0].productionBuilding.id]
                }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.target-unknown");
        });
    }),
    ("unit synergy accepts registered native and cross-pack targets", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies =
            [
                new ContentUnitSynergyDefinition
                {
                    id = "sample:test-synergy-native",
                    statName = "damage",
                    multiplier = 1,
                    radius = 1,
                    targetUnitIds = ["NativeWalker", "other:registered-unit"]
                }
            ];
            ExpectValid(pack, root,
                new SnapshotWithUnits(["NativeWalker", "other:registered-unit"]), new KnownModuleValidator());
        });
    }),
    ("unit upgrade requires a known target unit", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:unknown-unit", requiredExperienceLevel = 1, cost = 10 }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.target-unknown");
        });
    }),
    ("unit upgrade rejects negative cost", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].upgrades =
            [
                new ContentUnitUpgradeDefinition { targetUnitId = pack.units[0].id, requiredExperienceLevel = 1, cost = -5 }
            ];
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.cost-invalid");
        });
    }),
    ("unit synergy rejects non-finite radius", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition { id = "sample:bad-radius", statName = "Damage1", radius = Single.PositiveInfinity }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.radius-invalid");
        });
    }),
    ("cross-pack unit upgrade graph rejects a cycle", () =>
    {
        var packs = new[]
        {
            new ContentPackDefinition
            {
                packId = "test:alpha-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:alpha-unit",
                        upgrades = new[]
                        {
                            new ContentUnitUpgradeDefinition { targetUnitId = "test:beta-unit" }
                        }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:beta-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:beta-unit",
                        upgrades = new[]
                        {
                            new ContentUnitUpgradeDefinition { targetUnitId = "test:alpha-unit" }
                        }
                    }
                }
            }
        };

        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (!issues.Any(issue => issue.PackId == "test:alpha-pack" &&
                issue.Diagnostic.Code == "unit-upgrade.cycle-detected") ||
            !issues.Any(issue => issue.PackId == "test:beta-pack" &&
                issue.Diagnostic.Code == "unit-upgrade.cycle-detected"))
        {
            throw new InvalidOperationException("A cross-pack upgrade cycle must identify both owning packs.");
        }
    }),
    ("cross-pack unit synergy graph rejects a cycle", () =>
    {
        ContentPackDefinition[] packs = CreateCrossPackUnitChain(2, synergy: true, closeCycle: true);
        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (!issues.Any(issue => issue.PackId == "test:alpha-pack" &&
                issue.Diagnostic.Code == "unit-synergy.cycle-detected") ||
            !issues.Any(issue => issue.PackId == "test:beta-pack" &&
                issue.Diagnostic.Code == "unit-synergy.cycle-detected"))
        {
            throw new InvalidOperationException("A cross-pack synergy cycle must identify both owning packs.");
        }
    }),
    ("cross-pack unit upgrade graph accepts exactly five edges", () =>
    {
        ContentPackDefinition[] packs = CreateCrossPackUnitChain(5, synergy: false);
        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException("A five-edge cross-pack upgrade chain must be accepted without diagnostics: " +
                string.Join(", ", issues.Select(issue => issue.Diagnostic.Code)));
        }
    }),
    ("cross-pack unit synergy graph rejects six edges", () =>
    {
        ContentPackDefinition[] packs = CreateCrossPackUnitChain(6, synergy: true);
        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (!issues.Any(issue => issue.PackId == "test:alpha-pack" &&
                issue.Diagnostic.Code == "unit-synergy.depth-exceeded" &&
                issue.Diagnostic.Path == "units[0].synergies"))
        {
            throw new InvalidOperationException("A six-edge cross-pack synergy chain must identify its origin pack and unit.");
        }
    }),
    ("cross-pack acyclic unit graph is accepted", () =>
    {
        ContentPackDefinition[] packs = CreateCrossPackUnitChain(4, synergy: false);
        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException("An acyclic cross-pack unit graph must validate without diagnostics.");
        }
    }),
    ("cross-pack unit graph ignores native targets and ambiguous IDs", () =>
    {
        var packs = new[]
        {
            new ContentPackDefinition
            {
                packId = "test:alpha-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:alpha-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "native:unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:beta-pack",
                units = new[]
                {
                    new ContentUnitDefinition
                    {
                        id = "test:beta-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:shared-unit" } }
                    },
                    new ContentUnitDefinition
                    {
                        id = "test:shared-unit",
                        upgrades = new[] { new ContentUnitUpgradeDefinition { targetUnitId = "test:beta-unit" } }
                    }
                }
            },
            new ContentPackDefinition
            {
                packId = "test:gamma-pack",
                units = new[] { new ContentUnitDefinition { id = "test:shared-unit" } }
            }
        };
        var issues = ContentPackValidator.ValidateCrossPackUnitReferenceGraphs(packs);
        if (issues.Count != 0)
        {
            throw new InvalidOperationException("Native or ambiguous IDs must not create graph edges.");
        }
    }),
    ("unit upgrade graph rejects circular references", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units = new ContentUnitDefinition[3];
            for (int i = 0; i < 3; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            pack.units[0].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-1" }
            };
            pack.units[1].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-2" }
            };
            pack.units[2].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-0" }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.cycle-detected");
        });
    }),
    ("unit upgrade graph rejects depth exceeded", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            for (int i = 0; i < count - 1; i++)
            {
                pack.units[i].upgrades = new[]
                {
                    new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-" + (i + 1) }
                };
            }
            pack.units[count - 1].upgrades = Array.Empty<ContentUnitUpgradeDefinition>();
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.depth-exceeded");
        });
    }),
    ("unit upgrade graph accepts exactly five edges", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].upgrades = i < count - 1
                    ? new[] { new ContentUnitUpgradeDefinition { targetUnitId = pack.units[i + 1].id } }
                    : Array.Empty<ContentUnitUpgradeDefinition>();
            }
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d => d.Code == "unit-upgrade.depth-exceeded"))
            {
                throw new InvalidOperationException("A five-edge upgrade chain must validate cleanly: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    ("unit upgrade depth check handles convergent paths", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:upgrade-join-" + i,
                    nativeTemplateId = "native:base",
                    upgrades = Array.Empty<ContentUnitUpgradeDefinition>()
                };
            }

            pack.units[0].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-5" },
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-1" }
            };
            for (int i = 1; i < 5; i++)
            {
                pack.units[i].upgrades = new[]
                {
                    new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-" + (i + 1) }
                };
            }
            pack.units[5].upgrades = new[]
            {
                new ContentUnitUpgradeDefinition { targetUnitId = "sample:upgrade-join-6" }
            };

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-upgrade.depth-exceeded");
        });
    }),
    ("unit synergy graph rejects circular references", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            pack.units = new ContentUnitDefinition[2];
            for (int i = 0; i < 2; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-" + i,
                    nativeTemplateId = "native:base"
                };
            }
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:s1",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-1" }
                }
            };
            pack.units[1].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:s2",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-0" }
                }
            };
            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.cycle-detected");
        });
    }),
    ("unit synergy graph rejects excessive chain depth", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-depth-" + i,
                    nativeTemplateId = "native:base"
                };
                pack.units[i].synergies = i < count - 1
                    ? new[]
                    {
                        new ContentUnitSynergyDefinition
                        {
                            id = "sample:synergy-depth-link-" + i,
                            statName = "damage",
                            multiplier = 1.0f,
                            radius = 1.0f,
                            targetUnitIds = new[] { "sample:synergy-depth-" + (i + 1) }
                        }
                    }
                    : Array.Empty<ContentUnitSynergyDefinition>();
            }

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.depth-exceeded");
        });
    }),
    ("unit synergy depth check handles convergent paths", () =>
    {
        WithPackage(sample, (pack, root) =>
        {
            const int count = 7;
            pack.units = new ContentUnitDefinition[count];
            for (int i = 0; i < count; i++)
            {
                pack.units[i] = new ContentUnitDefinition
                {
                    id = "sample:synergy-join-" + i,
                    nativeTemplateId = "native:base"
                };
                pack.units[i].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            }

            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:synergy-join-root",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-join-5", "sample:synergy-join-1" }
                }
            };
            for (int i = 1; i < 5; i++)
            {
                pack.units[i].synergies = new[]
                {
                    new ContentUnitSynergyDefinition
                    {
                        id = "sample:synergy-join-link-" + i,
                        statName = "damage",
                        multiplier = 1.0f,
                        radius = 1.0f,
                        targetUnitIds = new[] { "sample:synergy-join-" + (i + 1) }
                    }
                };
            }
            pack.units[5].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:synergy-join-tail",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { "sample:synergy-join-6" }
                }
            };

            ExpectDiagnostic(pack, root, new EmptySnapshot(), new KnownModuleValidator(),
                "unit-synergy.depth-exceeded");
        });
    }),
    ("unit synergy graph accepts exactly five edges", () =>
        {
            WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 6;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].synergies = i < count - 1
                    ? new[]
                    {
                        new ContentUnitSynergyDefinition
                        {
                            id = "sample:synergy-boundary-link-" + i,
                            statName = "damage", multiplier = 1.0f, radius = 1.0f,
                            targetUnitIds = new[] { pack.units[i + 1].id }
                        }
                    }
                    : Array.Empty<ContentUnitSynergyDefinition>();
            }
            ContentPackValidationResult result = ContentPackValidator.Validate(
                pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d => d.Code == "unit-synergy.depth-exceeded"))
            {
                throw new InvalidOperationException("A five-edge synergy chain must validate cleanly: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    ("unit synergy graph accepts convergent DAG", () =>
    {

        WithPackage(nativeSample, (pack, root) =>
        {
            const int count = 4;
            for (int i = 0; i < count; i++)
            {
                pack.units[i].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            }
            pack.units[0].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d1",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[1].id, pack.units[2].id }
                }
            };
            pack.units[1].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d2",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[3].id }
                }
            };
            pack.units[2].synergies = new[]
            {
                new ContentUnitSynergyDefinition
                {
                    id = "sample:d3",
                    statName = "damage",
                    multiplier = 1.0f,
                    radius = 1.0f,
                    targetUnitIds = new[] { pack.units[3].id }
                }
            };
            pack.units[3].synergies = Array.Empty<ContentUnitSynergyDefinition>();
            ContentPackValidationResult result = ContentPackValidator.Validate(pack, root, new EmptySnapshot(), new KnownModuleValidator());
            if (!result.IsValid || result.Diagnostics.Any(d =>
                    d.Code == "unit-synergy.cycle-detected" || d.Code == "unit-synergy.depth-exceeded"))
            {
                throw new InvalidOperationException("Convergent DAG must be fully valid without cycle/depth diagnostics: " +
                    string.Join(", ", result.Diagnostics.Select(d => d.Code)));
            }
        });
    }),
    };
}
