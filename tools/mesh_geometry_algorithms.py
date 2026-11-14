#!/usr/bin/env python3
"""Open-source 3D mesh processing, fairing, LOD simplification, and vertex-wear algorithms.

Pure NumPy implementations compatible with Blender 4.5 ``bpy`` meshes and offline
FBX/OBJ processing without requiring external proprietary SDKs:
  - Taubin lambda|mu volume-preserving surface smoothing (Taubin 1995)
  - Garland-Heckbert Quadric Error Metric (QEM) edge-collapse decimation
  - Tom Forsyth linear-speed LRU vertex-cache triangle reordering (ACMR)
  - Geometry-driven Rogue Command ``VehiclePBR_VertexWear`` RGBA synthesis
    (R: Cavity AO, G: Convex Edge Wear >= 35 deg, B: Thermal Soot, A: Team Mask)
"""

from __future__ import annotations

import heapq
import math
from collections import defaultdict
from typing import Any

import numpy as np


def _compute_face_normals_and_areas(
    vertices: np.ndarray, faces: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    v0 = vertices[faces[:, 0]]
    v1 = vertices[faces[:, 1]]
    v2 = vertices[faces[:, 2]]
    cross = np.cross(v1 - v0, v2 - v0)
    norms = np.linalg.norm(cross, axis=1)
    areas = 0.5 * norms
    safe_norms = np.where(norms > 1e-12, norms, 1.0)[:, None]
    normals = cross / safe_norms
    return normals, areas


def _compute_vertex_normals(vertices: np.ndarray, faces: np.ndarray) -> np.ndarray:
    v0 = vertices[faces[:, 0]]
    v1 = vertices[faces[:, 1]]
    v2 = vertices[faces[:, 2]]
    face_cross = np.cross(v1 - v0, v2 - v0)
    v_normals = np.zeros_like(vertices, dtype=np.float64)
    for k in range(3):
        np.add.at(v_normals, faces[:, k], face_cross)
    norms = np.linalg.norm(v_normals, axis=1, keepdims=True)
    return v_normals / np.maximum(norms, 1e-12)


def _find_boundary_vertices(faces: np.ndarray, num_vertices: int) -> np.ndarray:
    edge_counts: dict[tuple[int, int], int] = defaultdict(int)
    for f in faces:
        a, b, c = int(f[0]), int(f[1]), int(f[2])
        for u, v in ((a, b), (b, c), (c, a)):
            key = (u, v) if u < v else (v, u)
            edge_counts[key] += 1
    boundary = np.zeros(num_vertices, dtype=bool)
    for (u, v), count in edge_counts.items():
        if count == 1:
            boundary[u] = True
            boundary[v] = True
    return boundary


def taubin_smooth(
    vertices: np.ndarray,
    faces: np.ndarray,
    iterations: int = 6,
    lam: float = 0.5,
    mu: float = -0.53,
    preserve_boundary: bool = True,
) -> np.ndarray:
    """Volume-preserving Taubin (lambda | mu) surface fairing on a triangle mesh."""
    verts = np.asarray(vertices, dtype=np.float64).copy()
    tris = np.asarray(faces, dtype=np.int64)
    n = len(verts)
    if n == 0 or len(tris) == 0 or iterations <= 0:
        return verts

    neighbors: list[set[int]] = [set() for _ in range(n)]
    for f in tris:
        a, b, c = int(f[0]), int(f[1]), int(f[2])
        neighbors[a].update((b, c))
        neighbors[b].update((a, c))
        neighbors[c].update((a, b))

    boundary = _find_boundary_vertices(tris, n) if preserve_boundary else np.zeros(n, dtype=bool)

    def _laplacian_step(pts: np.ndarray, factor: float) -> np.ndarray:
        nxt = pts.copy()
        for idx in range(n):
            if boundary[idx] or not neighbors[idx]:
                continue
            nbr_idx = list(neighbors[idx])
            avg = pts[nbr_idx].mean(axis=0)
            nxt[idx] = pts[idx] + factor * (avg - pts[idx])
        return nxt

    for _ in range(iterations):
        verts = _laplacian_step(verts, lam)
        verts = _laplacian_step(verts, mu)
    return verts


def qem_simplify(
    vertices: np.ndarray,
    faces: np.ndarray,
    target_faces: int,
    boundary_weight: float = 10.0,
) -> tuple[np.ndarray, np.ndarray]:
    """Garland-Heckbert Quadric Error Metric (QEM) iterative edge-collapse decimation.

    Preserves boundary edges with elevated plane-quadric penalties and returns
    compacted ``(vertices, faces)``.
    """
    verts = np.asarray(vertices, dtype=np.float64).copy()
    tris = np.asarray(faces, dtype=np.int64).copy()
    if target_faces <= 0 or len(tris) <= target_faces:
        return verts, tris

    n_verts = len(verts)
    quadrics = np.zeros((n_verts, 4, 4), dtype=np.float64)

    face_normals, _ = _compute_face_normals_and_areas(verts, tris)
    for idx, f in enumerate(tris):
        n = face_normals[idx]
        if np.linalg.norm(n) < 1e-8:
            continue
        d = -float(np.dot(n, verts[f[0]]))
        p = np.array([n[0], n[1], n[2], d], dtype=np.float64)
        kp = np.outer(p, p)
        for vi in f:
            quadrics[vi] += kp

    # Boundary edge penalty planes
    edge_faces: dict[tuple[int, int], list[int]] = defaultdict(list)
    for fi, f in enumerate(tris):
        for i in range(3):
            u, v = int(f[i]), int(f[(i + 1) % 3])
            key = (u, v) if u < v else (v, u)
            edge_faces[key].append(fi)

    for (u, v), flist in edge_faces.items():
        if len(flist) == 1:
            fn = face_normals[flist[0]]
            edge_vec = verts[v] - verts[u]
            bn = np.cross(edge_vec, fn)
            bn_norm = float(np.linalg.norm(bn))
            if bn_norm > 1e-10:
                bn /= bn_norm
                d = -float(np.dot(bn, verts[u]))
                p = np.array([bn[0], bn[1], bn[2], d], dtype=np.float64)
                kp = np.outer(p, p) * float(boundary_weight)
                quadrics[u] += kp
                quadrics[v] += kp

    parent = list(range(n_verts))

    def _find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def _edge_cost(u: int, v: int) -> tuple[float, np.ndarray]:
        q = quadrics[u] + quadrics[v]
        q_mat = q[:3, :3]
        if abs(float(np.linalg.det(q_mat))) > 1e-10:
            try:
                opt = np.linalg.solve(q_mat, -q[:3, 3])
                v4 = np.array([opt[0], opt[1], opt[2], 1.0], dtype=np.float64)
                cost = float(v4 @ q @ v4)
                return max(0.0, cost), opt
            except np.linalg.LinAlgError:
                pass
        # Fallback: best of u, v, midpoint
        best_cost = float("inf")
        best_pos = 0.5 * (verts[u] + verts[v])
        for cand in (verts[u], verts[v], best_pos):
            v4 = np.array([cand[0], cand[1], cand[2], 1.0], dtype=np.float64)
            c = max(0.0, float(v4 @ q @ v4))
            if c < best_cost:
                best_cost = c
                best_pos = cand
        return best_cost, best_pos

    heap: list[tuple[float, int, int, int]] = []
    version = [0] * n_verts
    adj: list[set[int]] = [set() for _ in range(n_verts)]
    for u, v in edge_faces:
        adj[u].add(v)
        adj[v].add(u)
        cost, _ = _edge_cost(u, v)
        heapq.heappush(heap, (cost, u, v, 0))

    active_faces = len(tris)
    tri_list = tris.tolist()
    face_alive = [True] * len(tri_list)
    vert_faces: list[set[int]] = [set() for _ in range(n_verts)]
    for fi, f in enumerate(tri_list):
        for vi in f:
            vert_faces[vi].add(fi)

    while heap and active_faces > target_faces:
        cost, u_raw, v_raw, ver = heapq.heappop(heap)
        u = _find(u_raw)
        v = _find(v_raw)
        if u == v or ver != version[u] + version[v]:
            continue
        if u > v:
            u, v = v, u

        _, new_pos = _edge_cost(u, v)
        verts[u] = new_pos
        quadrics[u] = quadrics[u] + quadrics[v]
        parent[v] = u
        version[u] += 1
        version[v] += 1

        # Rewire faces incident to v
        for fi in list(vert_faces[v]):
            if not face_alive[fi]:
                continue
            f = tri_list[fi]
            new_f = [u if _find(x) == u or _find(x) == v else _find(x) for x in f]
            if len(set(new_f)) < 3:
                face_alive[fi] = False
                active_faces -= 1
            else:
                tri_list[fi] = new_f
                vert_faces[u].add(fi)
        vert_faces[v].clear()

        # Merge adjacency
        for nbr_raw in list(adj[v]):
            nbr = _find(nbr_raw)
            if nbr != u and nbr != v:
                adj[u].add(nbr)
                adj[nbr].add(u)
        adj[v].clear()

        for nbr_raw in list(adj[u]):
            nbr = _find(nbr_raw)
            if nbr == u:
                continue
            c, _ = _edge_cost(u, nbr)
            a, b = (u, nbr) if u < nbr else (nbr, u)
            heapq.heappush(heap, (c, a, b, version[a] + version[b]))

    kept_faces = np.array(
        [[_find(x) for x in tri_list[fi]] for fi, alive in enumerate(face_alive) if alive],
        dtype=np.int64,
    )
    if len(kept_faces) == 0:
        return np.zeros((0, 3), dtype=np.float64), np.zeros((0, 3), dtype=np.int64)

    unique_verts, inverse = np.unique(kept_faces.ravel(), return_inverse=True)
    compacted_verts = verts[unique_verts]
    compacted_faces = inverse.reshape(-1, 3)
    return compacted_verts, compacted_faces


def compute_acmr(faces: np.ndarray, cache_size: int = 32) -> float:
    """Compute Average Cache Miss Ratio (misses / triangles) for an LRU vertex cache."""
    if len(faces) == 0:
        return 0.0
    cache: list[int] = []
    misses = 0
    for f in faces:
        for vi in f:
            v = int(vi)
            if v in cache:
                cache.remove(v)
                cache.insert(0, v)
            else:
                misses += 1
                cache.insert(0, v)
                if len(cache) > cache_size:
                    cache.pop()
    return float(misses) / float(len(faces))


def compute_forsyth_face_order(
    faces: np.ndarray,
    num_vertices: int,
    cache_size: int = 32,
) -> tuple[np.ndarray, float]:
    """Return Forsyth LRU vertex-cache triangle permutation indices and resulting ACMR."""
    tris = np.asarray(faces, dtype=np.int64)
    num_tris = len(tris)
    if num_tris == 0:
        return np.zeros((0,), dtype=np.int64), 0.0

    # Constants from Forsyth's paper
    cache_decay_power = 1.5
    last_tri_score = 0.75
    valence_boost_scale = 2.0
    valence_boost_power = 0.5

    vert_tris: list[list[int]] = [[] for _ in range(num_vertices)]
    for ti, f in enumerate(tris):
        for vi in f:
            vert_tris[int(vi)].append(ti)

    active_valence = [len(vt) for vt in vert_tris]
    cache_pos = [-1] * num_vertices
    vert_score = [0.0] * num_vertices

    def _calc_vert_score(vi: int) -> float:
        val = active_valence[vi]
        if val == 0:
            return -1.0
        pos = cache_pos[vi]
        if pos < 0:
            score = 0.0
        elif pos < 3:
            score = last_tri_score
        else:
            scaler = 1.0 / float(max(1, cache_size - 3))
            score = max(0.0, 1.0 - float(pos - 3) * scaler) ** cache_decay_power
        valence_boost = valence_boost_scale * (float(val) ** (-valence_boost_power))
        return score + valence_boost

    for vi in range(num_vertices):
        vert_score[vi] = _calc_vert_score(vi)

    tri_added = [False] * num_tris
    tri_score = [
        vert_score[int(f[0])] + vert_score[int(f[1])] + vert_score[int(f[2])]
        for f in tris
    ]

    cache: list[int] = []
    out_order: list[int] = []
    best_tri = int(np.argmax(tri_score))

    while len(out_order) < num_tris:
        if best_tri < 0 or tri_added[best_tri]:
            # Scan for highest remaining triangle
            best_s = -1.0
            best_tri = -1
            for ti in range(num_tris):
                if not tri_added[ti] and tri_score[ti] > best_s:
                    best_s = tri_score[ti]
                    best_tri = ti
            if best_tri < 0:
                break

        tri_added[best_tri] = True
        out_order.append(best_tri)
        f = tris[best_tri]

        touched_verts: set[int] = set()
        for vi_raw in f:
            vi = int(vi_raw)
            active_valence[vi] -= 1
            if vi in cache:
                cache.remove(vi)
            cache.insert(0, vi)
            touched_verts.add(vi)

        # Trim cache beyond cache_size + 3 grace window
        while len(cache) > cache_size:
            evicted = cache.pop()
            cache_pos[evicted] = -1
            touched_verts.add(evicted)

        for pos, vi in enumerate(cache):
            cache_pos[vi] = pos
            touched_verts.add(vi)

        affected_tris: set[int] = set()
        for vi in touched_verts:
            vert_score[vi] = _calc_vert_score(vi)
            for ti in vert_tris[vi]:
                if not tri_added[ti]:
                    affected_tris.add(ti)

        best_next = -1
        best_next_score = -1.0
        for ti in affected_tris:
            tf = tris[ti]
            s = vert_score[int(tf[0])] + vert_score[int(tf[1])] + vert_score[int(tf[2])]
            tri_score[ti] = s
            if s > best_next_score:
                best_next_score = s
                best_next = ti
        best_tri = best_next

    order_arr = np.array(out_order, dtype=np.int64)
    return order_arr, compute_acmr(tris[order_arr], cache_size=cache_size)


def optimize_vertex_cache_forsyth(
    faces: np.ndarray,
    num_vertices: int,
    cache_size: int = 32,
) -> tuple[np.ndarray, float]:
    """Tom Forsyth (2006) linear-speed LRU post-transform vertex cache optimization."""
    tris = np.asarray(faces, dtype=np.int64)
    if len(tris) == 0:
        return tris.copy(), 0.0
    order_arr, acmr = compute_forsyth_face_order(tris, num_vertices, cache_size=cache_size)
    return tris[order_arr], acmr


def compute_vertex_wear_rgba(
    vertices: np.ndarray,
    faces: np.ndarray,
    soot_anchors: np.ndarray | None = None,
    soot_radius: float = 0.6,
    edge_angle_deg: float = 35.0,
    team_mask: np.ndarray | None = None,
) -> np.ndarray:
    """Synthesize Rogue Command ``VehiclePBR_VertexWear`` RGBA vertex colors.

    Contract:
      - R (Cavity AO): 1.0 on open/zenith faces, 0.25..0.40 on undersides/cavities.
      - G (Edge Wear): 0.0 on flat/concave surfaces; 0.7..1.0 on convex dihedral >= 35 deg.
      - B (Thermal Soot): 0.0 default; quadratic falloff (1 - d/R)^2 near soot anchors.
      - A (Team Mask): preserved ``team_mask`` or 1.0 default.
    """
    verts = np.asarray(vertices, dtype=np.float64)
    tris = np.asarray(faces, dtype=np.int64)
    n_verts = len(verts)
    rgba = np.zeros((n_verts, 4), dtype=np.float32)
    if n_verts == 0 or len(tris) == 0:
        rgba[:, 0] = 1.0
        rgba[:, 3] = 1.0
        return rgba

    face_normals, _ = _compute_face_normals_and_areas(verts, tris)
    vert_normals = _compute_vertex_normals(verts, tris)

    # R: Cavity AO combining zenith orientation (Z up in Blender) and local concavity
    z_min, z_max = float(verts[:, 2].min()), float(verts[:, 2].max())
    z_norm = (verts[:, 2] - z_min) / max(z_max - z_min, 1e-6)
    zenith = 0.5 * (vert_normals[:, 2] + 1.0)
    cavity_ao = np.clip(0.28 + 0.42 * zenith + 0.30 * z_norm, 0.25, 1.0)
    rgba[:, 0] = cavity_ao.astype(np.float32)

    # G: Convex edge wear from dihedral angle between adjacent faces
    edge_faces: dict[tuple[int, int], list[int]] = defaultdict(list)
    for fi, f in enumerate(tris):
        for i in range(3):
            u, v = int(f[i]), int(f[(i + 1) % 3])
            key = (u, v) if u < v else (v, u)
            edge_faces[key].append(fi)

    cos_thresh = math.cos(math.radians(edge_angle_deg))
    edge_wear = np.zeros(n_verts, dtype=np.float32)
    for (u, v), flist in edge_faces.items():
        if len(flist) != 2:
            continue
        f0, f1 = flist[0], flist[1]
        n0, n1 = face_normals[f0], face_normals[f1]
        dot_n = float(np.clip(np.dot(n0, n1), -1.0, 1.0))
        if dot_n <= cos_thresh:
            # Check convexity: vector from face0 centroid to face1 centroid opposes n0
            c0 = verts[tris[f0]].mean(axis=0)
            c1 = verts[tris[f1]].mean(axis=0)
            is_convex = float(np.dot(c1 - c0, n0)) < -1e-6
            if is_convex:
                angle_deg = math.degrees(math.acos(dot_n))
                wear_val = min(1.0, 0.70 + 0.30 * ((angle_deg - edge_angle_deg) / max(90.0 - edge_angle_deg, 1.0)))
                edge_wear[u] = max(edge_wear[u], wear_val)
                edge_wear[v] = max(edge_wear[v], wear_val)
    rgba[:, 1] = edge_wear

    # B: Thermal soot with quadratic dropoff (1 - d/R)^2
    if soot_anchors is not None and len(soot_anchors) > 0 and soot_radius > 0.0:
        anchors = np.asarray(soot_anchors, dtype=np.float64).reshape(-1, 3)
        dists = np.linalg.norm(verts[:, None, :] - anchors[None, :, :], axis=-1).min(axis=1)
        t = np.clip(1.0 - dists / float(soot_radius), 0.0, 1.0)
        rgba[:, 2] = (t * t).astype(np.float32)

    # A: Team mask
    if team_mask is not None:
        rgba[:, 3] = np.clip(np.asarray(team_mask, dtype=np.float32), 0.0, 1.0)
    else:
        rgba[:, 3] = 1.0
    return rgba


def compute_mesh_quality_metrics(
    vertices: np.ndarray, faces: np.ndarray, cache_size: int = 32
) -> dict[str, Any]:
    """Compute geometric sanity, manifoldness, and vertex-cache ACMR metrics."""
    verts = np.asarray(vertices, dtype=np.float64)
    tris = np.asarray(faces, dtype=np.int64)
    _, areas = _compute_face_normals_and_areas(verts, tris) if len(tris) else (np.zeros((0, 3)), np.zeros(0))
    degenerate = int(np.sum(areas <= 1e-10))

    edge_counts: dict[tuple[int, int], int] = defaultdict(int)
    for f in tris:
        for i in range(3):
            u, v = int(f[i]), int(f[(i + 1) % 3])
            key = (u, v) if u < v else (v, u)
            edge_counts[key] += 1
    boundary_edges = sum(1 for c in edge_counts.values() if c == 1)
    non_manifold_edges = sum(1 for c in edge_counts.values() if c > 2)

    acmr_before = compute_acmr(tris, cache_size=cache_size)
    _, acmr_after = optimize_vertex_cache_forsyth(tris, len(verts), cache_size=cache_size)
    return {
        "vertexCount": int(len(verts)),
        "triangleCount": int(len(tris)),
        "surfaceArea": float(np.sum(areas)),
        "degenerateTriangles": degenerate,
        "boundaryEdges": int(boundary_edges),
        "nonManifoldEdges": int(non_manifold_edges),
        "acmrBefore": round(acmr_before, 4),
        "acmrAfter": round(acmr_after, 4),
    }


def extract_mesh_arrays_bpy(obj: Any) -> tuple[np.ndarray, np.ndarray]:
    """Extract triangulated (vertices, faces) NumPy arrays from a Blender mesh object."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    verts = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
    tris = np.array([lt.vertices[:] for lt in mesh.loop_triangles], dtype=np.int64)
    return verts, tris


def apply_taubin_smooth_bpy(
    obj: Any,
    iterations: int = 6,
    lam: float = 0.5,
    mu: float = -0.53,
    preserve_boundary: bool = True,
) -> np.ndarray:
    """Apply volume-preserving Taubin smoothing directly to a Blender mesh object."""
    verts, tris = extract_mesh_arrays_bpy(obj)
    smoothed = taubin_smooth(
        verts, tris, iterations=iterations, lam=lam, mu=mu, preserve_boundary=preserve_boundary
    )
    for idx, v in enumerate(obj.data.vertices):
        v.co = (float(smoothed[idx, 0]), float(smoothed[idx, 1]), float(smoothed[idx, 2]))
    obj.data.update()
    return smoothed


def apply_qem_simplify_bpy(
    obj: Any,
    ratio: float = 0.5,
    boundary_weight: float = 10.0,
) -> tuple[int, int]:
    """Simplify a Blender mesh object in-place using Garland-Heckbert QEM decimation."""
    verts, tris = extract_mesh_arrays_bpy(obj)
    orig_count = len(tris)
    target = max(4, int(round(orig_count * max(0.05, min(1.0, float(ratio))))))
    new_verts, new_faces = qem_simplify(verts, tris, target_faces=target, boundary_weight=boundary_weight)
    mesh = obj.data
    mesh.clear_geometry()
    mesh.from_pydata(new_verts.tolist(), [], new_faces.tolist())
    mesh.update()
    return orig_count, len(new_faces)


def apply_forsyth_cache_optimize_bpy(obj: Any, cache_size: int = 32) -> float:
    """Reorder a Blender mesh's triangles via Forsyth LRU optimization while preserving
    UV layers, Color RGBA attributes, material indices, smooth shading, vertex groups,
    and custom split normals.
    """
    import bmesh

    mesh = obj.data
    if len(mesh.polygons) == 0:
        return 0.0

    # Ensure pure triangle topology while preserving UVs, Color RGBA, materials, and weights
    if any(len(poly.vertices) != 3 for poly in mesh.polygons):
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

    n_verts = len(mesh.vertices)
    n_tris = len(mesh.polygons)
    if n_tris == 0:
        return 0.0

    verts = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
    tris = np.array([poly.vertices[:] for poly in mesh.polygons], dtype=np.int64)
    mat_indices = np.array([poly.material_index for poly in mesh.polygons], dtype=np.int32)
    smooth_flags = np.array([poly.use_smooth for poly in mesh.polygons], dtype=bool)

    # Snapshot UV layers (n_tris, 3, 2)
    uv_snapshots: list[tuple[str, bool, np.ndarray]] = []
    for uv_layer in mesh.uv_layers:
        uv_arr = np.zeros((n_tris, 3, 2), dtype=np.float32)
        for pi, poly in enumerate(mesh.polygons):
            for ci, li in enumerate(poly.loop_indices):
                uv_arr[pi, ci] = uv_layer.data[li].uv[:]
        uv_snapshots.append((uv_layer.name, uv_layer.active_render, uv_arr))

    # Snapshot Color attributes (CORNER or POINT)
    color_snapshots: list[tuple[str, str, str, np.ndarray]] = []
    active_color_name = mesh.color_attributes.active_color.name if mesh.color_attributes.active_color else None
    for attr in mesh.color_attributes:
        if attr.domain == "CORNER":
            c_arr = np.zeros((n_tris, 3, 4), dtype=np.float32)
            for pi, poly in enumerate(mesh.polygons):
                for ci, li in enumerate(poly.loop_indices):
                    c_arr[pi, ci] = attr.data[li].color[:]
        else:
            c_arr = np.array([attr.data[vi].color[:] for vi in range(n_verts)], dtype=np.float32)
        color_snapshots.append((attr.name, attr.data_type, attr.domain, c_arr))

    # Snapshot custom corner normals if present
    has_custom_normals = bool(getattr(mesh, "has_custom_normals", False))
    corner_normals = None
    if has_custom_normals:
        corner_normals = np.zeros((n_tris, 3, 3), dtype=np.float32)
        for pi, poly in enumerate(mesh.polygons):
            for ci, li in enumerate(poly.loop_indices):
                corner_normals[pi, ci] = mesh.corner_normals[li].vector[:]

    # Snapshot vertex group weights per vertex
    vgroup_names = [vg.name for vg in obj.vertex_groups]
    vgroup_weights: list[list[tuple[str, float]]] = [[] for _ in range(n_verts)]
    if vgroup_names:
        idx_to_name = {vg.index: vg.name for vg in obj.vertex_groups}
        for vi, v in enumerate(mesh.vertices):
            for g in v.groups:
                if g.group in idx_to_name and g.weight > 0.0:
                    vgroup_weights[vi].append((idx_to_name[g.group], float(g.weight)))

    saved_materials = list(mesh.materials)
    order, acmr = compute_forsyth_face_order(tris, n_verts, cache_size=cache_size)
    opt_tris = tris[order]

    mesh.clear_geometry()
    mesh.from_pydata(verts.tolist(), [], opt_tris.tolist())
    if len(mesh.materials) == 0:
        for mat in saved_materials:
            mesh.materials.append(mat)

    for pi, poly in enumerate(mesh.polygons):
        src_pi = int(order[pi])
        poly.material_index = int(mat_indices[src_pi])
        poly.use_smooth = bool(smooth_flags[src_pi])

    for uv_name, is_active_render, uv_arr in uv_snapshots:
        uv_layer = mesh.uv_layers.new(name=uv_name, do_init=False)
        uv_layer.active_render = is_active_render
        opt_uv = uv_arr[order]
        for pi, poly in enumerate(mesh.polygons):
            for ci, li in enumerate(poly.loop_indices):
                uv_layer.data[li].uv = (float(opt_uv[pi, ci, 0]), float(opt_uv[pi, ci, 1]))

    for attr_name, data_type, domain, c_arr in color_snapshots:
        attr = mesh.color_attributes.new(name=attr_name, type=data_type, domain=domain)
        if domain == "CORNER":
            opt_c = c_arr[order]
            for pi, poly in enumerate(mesh.polygons):
                for ci, li in enumerate(poly.loop_indices):
                    attr.data[li].color = tuple(float(x) for x in opt_c[pi, ci])
        else:
            for vi in range(n_verts):
                attr.data[vi].color = tuple(float(x) for x in c_arr[vi])
        if attr_name == active_color_name:
            mesh.color_attributes.active_color = attr

    if vgroup_names:
        for vi, weights in enumerate(vgroup_weights):
            for vg_name, weight in weights:
                vg = obj.vertex_groups.get(vg_name)
                if vg is not None:
                    vg.add([vi], weight, "REPLACE")

    if corner_normals is not None:
        opt_n = corner_normals[order].reshape(-1, 3)
        mesh.normals_split_custom_set([tuple(float(x) for x in n) for n in opt_n])

    mesh.update()
    return acmr


def audit_blender_object_metrics_bpy(obj: Any, cache_size: int = 32) -> dict[str, Any]:
    """Inspect a Blender mesh object for topology health, ACMR, UVs, and Vertex-Wear RGBA."""
    verts, tris = extract_mesh_arrays_bpy(obj)
    metrics = compute_mesh_quality_metrics(verts, tris, cache_size=cache_size)
    mesh = obj.data
    metrics["hasUvMap"] = len(mesh.uv_layers) > 0
    metrics["colorAttributeCount"] = len(mesh.color_attributes)
    color_attr = mesh.color_attributes.get("Color")
    if color_attr is not None and len(color_attr.data) > 0:
        cols = np.array([d.color[:] for d in color_attr.data], dtype=np.float32)
        metrics["vertexWearRgba"] = {
            "rMin": round(float(cols[:, 0].min()), 3),
            "rMax": round(float(cols[:, 0].max()), 3),
            "gMax": round(float(cols[:, 1].max()), 3),
            "bMax": round(float(cols[:, 2].max()), 3),
            "aMin": round(float(cols[:, 3].min()), 3),
            "aMax": round(float(cols[:, 3].max()), 3),
        }
    else:
        metrics["vertexWearRgba"] = None
    return metrics


def apply_vertex_wear_rgba_bpy(
    obj: Any,
    soot_anchors: np.ndarray | None = None,
    soot_radius: float = 0.6,
    edge_angle_deg: float = 35.0,
    attribute_name: str = "Color",
) -> np.ndarray:
    """Write Rogue Command ``VehiclePBR_VertexWear`` RGBA vertex colors onto a Blender mesh."""
    verts, tris = extract_mesh_arrays_bpy(obj)
    rgba = compute_vertex_wear_rgba(
        verts, tris, soot_anchors=soot_anchors, soot_radius=soot_radius, edge_angle_deg=edge_angle_deg
    )
    mesh = obj.data
    attr = mesh.color_attributes.get(attribute_name)
    if attr is None:
        attr = mesh.color_attributes.new(name=attribute_name, type="BYTE_COLOR", domain="CORNER")
    if attr.domain == "CORNER":
        for poly in mesh.polygons:
            for loop_idx in poly.loop_indices:
                vi = mesh.loops[loop_idx].vertex_index
                attr.data[loop_idx].color = (
                    float(rgba[vi, 0]),
                    float(rgba[vi, 1]),
                    float(rgba[vi, 2]),
                    float(rgba[vi, 3]),
                )
    else:
        for vi in range(len(verts)):
            attr.data[vi].color = (
                float(rgba[vi, 0]),
                float(rgba[vi, 1]),
                float(rgba[vi, 2]),
                float(rgba[vi, 3]),
            )
    mesh.update()
    return rgba


def load_obj_mesh(path: Any) -> tuple[np.ndarray, np.ndarray]:
    """Minimal Wavefront .obj loader returning triangulated (vertices, faces)."""
    verts: list[list[float]] = []
    faces: list[list[int]] = []
    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            parts = line.strip().split()
            if not parts:
                continue
            if parts[0] == "v" and len(parts) >= 4:
                verts.append([float(parts[1]), float(parts[2]), float(parts[3])])
            elif parts[0] == "f" and len(parts) >= 4:
                idx = [int(p.split("/")[0]) - 1 for p in parts[1:]]
                for k in range(1, len(idx) - 1):
                    faces.append([idx[0], idx[k], idx[k + 1]])
    return np.array(verts, dtype=np.float64), np.array(faces, dtype=np.int64)


def save_obj_mesh(path: Any, vertices: np.ndarray, faces: np.ndarray) -> None:
    """Minimal Wavefront .obj writer for offline inspection."""
    with open(path, "w", encoding="utf-8") as fh:
        for v in vertices:
            fh.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")
        for f in faces:
            fh.write(f"f {int(f[0]) + 1} {int(f[1]) + 1} {int(f[2]) + 1}\n")


if __name__ == "__main__":
    import argparse
    import json
    from pathlib import Path

    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    insp = sub.add_parser("inspect", help="Compute mesh quality and ACMR metrics for an OBJ.")
    insp.add_argument("--input", type=Path, required=True)
    simp = sub.add_parser("simplify", help="QEM simplify an OBJ mesh to a target ratio.")
    simp.add_argument("--input", type=Path, required=True)
    simp.add_argument("--output", type=Path, required=True)
    simp.add_argument("--ratio", type=float, default=0.5)

    args = parser.parse_args()
    v_in, f_in = load_obj_mesh(args.input)
    if args.command == "inspect":
        print(json.dumps(compute_mesh_quality_metrics(v_in, f_in), indent=2))
    elif args.command == "simplify":
        target = max(4, int(round(len(f_in) * args.ratio)))
        v_out, f_out = qem_simplify(v_in, f_in, target_faces=target)
        f_opt, acmr = optimize_vertex_cache_forsyth(f_out, len(v_out))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        save_obj_mesh(args.output, v_out, f_opt)
        print(json.dumps({"inputFaces": len(f_in), "outputFaces": len(f_opt), "acmr": round(acmr, 4)}))
