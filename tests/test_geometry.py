"""DNA 기하 생성 테스트."""

import numpy as np

from crispr_studio.geometry import dna


def test_build_helix_shape():
    m = dna.build_helix("ACGTACGTAC")
    assert m.n_bases == 10
    assert m.strand_a.shape == (10, 3)
    assert m.strand_b.shape == (10, 3)
    assert m.axis.shape == (10, 3)


def test_helix_radius_and_opposite_strands():
    m = dna.build_helix("ACGT", radius=10.0)
    # 각 골격 점은 축(z)에서 반지름만큼 떨어져 있다
    r_a = np.linalg.norm(m.strand_a[:, :2], axis=1)
    assert np.allclose(r_a, 10.0)
    # 두 골격은 x,y 평면에서 서로 반대편(합이 원점 근처)
    assert np.allclose(m.strand_a[:, :2] + m.strand_b[:, :2], 0.0, atol=1e-9)


def test_centered_height():
    m = dna.build_helix("A" * 11)
    assert abs(m.axis[:, 2].mean()) < 1e-9
    assert np.isclose(m.total_height(), dna.RISE_PER_BP * 10)


def test_axis_interpolation():
    m = dna.build_helix("ACGTAC")
    p = m.axis_point(2.5)
    mid = (m.axis[2] + m.axis[3]) / 2
    assert np.allclose(p, mid)


def test_cut_gap_offsets():
    m = dna.build_helix("ACGTACGT")
    offs = dna.cut_gap_offsets(m, cut_index=4, gap=10.0)
    assert np.all(offs[:4, 2] == -5.0)
    assert np.all(offs[4:, 2] == 5.0)


def test_empty_sequence():
    m = dna.build_helix("")
    assert m.n_bases == 0
    assert m.axis_point(0).shape == (3,)


def test_base_color():
    m = dna.build_helix("ACGT")
    assert m.base_color(0) == dna.BASE_COLORS["A"]
