"""파싱된 구조 캐싱.

파일 경로+수정시각+옵션을 키로 피클 캐시를 둔다. 대형 구조(4OO8 등)의
재파싱과 결합 재계산을 피해 재로딩을 빠르게 한다.
"""

from __future__ import annotations

import hashlib
import pickle
from dataclasses import asdict
from pathlib import Path

from .loader import LoadOptions, load_structure
from .model import Structure

_DEFAULT_CACHE = Path.home() / ".cache" / "crispr_studio" / "structures"


def _cache_dir() -> Path:
    d = _DEFAULT_CACHE
    try:
        d.mkdir(parents=True, exist_ok=True)
    except OSError:  # pragma: no cover
        pass
    return d


def _key(path: Path, options: LoadOptions) -> str:
    try:
        mtime = path.stat().st_mtime_ns
    except OSError:
        mtime = 0
    raw = f"{path.resolve()}|{mtime}|{sorted(asdict(options).items())}"
    return hashlib.sha1(raw.encode()).hexdigest()[:16]


def load_cached(
    path: str | Path,
    *,
    name: str | None = None,
    options: LoadOptions | None = None,
    use_cache: bool = True,
) -> Structure:
    """캐시가 있으면 그걸, 없으면 파싱 후 캐시에 저장."""
    path = Path(path)
    options = options or LoadOptions()
    if not use_cache:
        return load_structure(path, name=name, options=options)

    cache_file = _cache_dir() / f"{path.stem}_{_key(path, options)}.pkl"
    if cache_file.exists():
        try:
            with cache_file.open("rb") as fh:
                return pickle.load(fh)
        except Exception:  # pragma: no cover - 손상 캐시 무시
            pass

    structure = load_structure(path, name=name, options=options)
    try:
        with cache_file.open("wb") as fh:
            pickle.dump(structure, fh, protocol=pickle.HIGHEST_PROTOCOL)
    except Exception:  # pragma: no cover
        pass
    return structure
