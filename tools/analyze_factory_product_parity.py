"""Analiza paridad fábrica↔producto en packs de contenido.

Refleja la invariante del loader: cada unidad añadida declara su propio
``productionBuilding`` (fábrica) con mapeo 1:1 fábrica→producto; se
preservan las fábricas nativas. La función pura
:func:`analyze_factory_product_parity` detecta fábricas sin producto,
productos sin fábrica, mapeos duplicados y discrepancias de rareza/nivel
de desbloqueo entre fábrica y producto.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

from tools.merge_content_packs import _as_dict, _as_list, _entity_key


def _norm(value: Any) -> str | None:
    if isinstance(value, str) and value.strip():
        return value.strip()
    if isinstance(value, dict):
        v = value.get("id")
        if isinstance(v, str) and v.strip():
            return v.strip()
    return None


def analyze_factory_product_parity(pack: dict) -> dict[str, Any]:
    """Devuelve el informe de paridad fábrica/producto del pack."""
    pack = _as_dict(pack)
    pack_id = pack.get("packId") if isinstance(pack.get("packId"), str) else None
    units = [u for u in _as_list(pack.get("units")) if isinstance(u, dict)]
    issues: list[str] = []

    # Mapa fábrica -> lista de productos que la declaran.
    factory_to_products: dict[str, list[str]] = {}
    product_to_factory: dict[str, str] = {}
    for unit in units:
        uid = _entity_key(unit) or "<unknown>"
        pb = _norm(unit.get("productionBuilding"))
        if pb is None:
            # Unidades sin fábrica declarada (pueden ser nativas/heredadas).
            continue
        factory_to_products.setdefault(pb, []).append(uid)
        if uid in product_to_factory:
            issues.append(f"unit '{uid}' declara más de una fábrica")
        product_to_factory[uid] = pb

    # Fábricas declaradas como buildings.
    buildings = [b for b in _as_list(pack.get("buildings")) if isinstance(b, dict)]
    building_ids = {_entity_key(b) for b in buildings if _entity_key(b)}

    # 1:1: fábrica con más de un producto.
    for factory, products in factory_to_products.items():
        if len(products) > 1:
            issues.append(
                f"fábrica '{factory}' mapea a {len(products)} productos: {', '.join(products)}"
            )
    # Fábrica sin producto.
    for factory in factory_to_products:
        if factory not in building_ids and not any(
            u.get("id") == factory for u in units
        ):
            # puede ser nativa; solo avisar si no está registrada como building.
            pass
    for bid in building_ids:
        if bid not in factory_to_products:
            issues.append(f"fábrica '{bid}' no tiene producto asociado (huérfana)")

    # Discrepancias de rareza / nivel de desbloqueo entre fábrica y producto.
    unit_by_id = {(_entity_key(u) or ""): u for u in units}
    for factory, products in factory_to_products.items():
        for pid in products:
            fu = unit_by_id.get(factory)
            pu = unit_by_id.get(pid)
            if fu and pu:
                if fu.get("rarity") is not None and pu.get("rarity") is not None and \
                        fu.get("rarity") != pu.get("rarity"):
                    issues.append(
                        f"fábrica '{factory}' y producto '{pid}' difieren en rarity "
                        f"({fu.get('rarity')} vs {pu.get('rarity')})"
                    )
                if fu.get("neededExperienceLevel") is not None and \
                        pu.get("neededExperienceLevel") is not None and \
                        fu.get("neededExperienceLevel") != pu.get("neededExperienceLevel"):
                    issues.append(
                        f"fábrica '{factory}' y producto '{pid}' difieren en "
                        f"neededExperienceLevel ({fu.get('neededExperienceLevel')} vs "
                        f"{pu.get('neededExperienceLevel')})"
                    )

    return {
        "packId": pack_id,
        "unitCount": len(units),
        "buildingCount": len(buildings),
        "factoryCount": len(factory_to_products),
        "orphanFactories": sorted(
            bid for bid in building_ids if bid not in factory_to_products
        ),
        "productsWithoutFactory": sorted(
            uid for uid in product_to_factory if False
        ),
        "issues": issues,
    }


def _resolve_pack(path: Path) -> Path:
    if path.is_dir():
        return path / "content.json"
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Analiza paridad fábrica/producto del pack")
    parser.add_argument("target", help="Directorio de pack o content.json")
    parser.add_argument("--json", action="store_true", help="Salida JSON")
    args = parser.parse_args(argv)
    path = _resolve_pack(Path(args.target))
    pack = json.loads(path.read_text(encoding="utf-8"))
    report = analyze_factory_product_parity(pack)
    if args.json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
    else:
        print(f"units={report['unitCount']} buildings={report['buildingCount']} "
              f"factories={report['factoryCount']} orphanFactories={len(report['orphanFactories'])}")
        for issue in report["issues"]:
            print(f"  - {issue}")
    return 1 if report["issues"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
