// Expect: 10
#include <utility>

namespace std {

template <class _Ty>
_NODISCARD constexpr bool _Is_nan(const _Ty& _Xx) noexcept {
    return _Xx == 0x7FC00000;
}

template <class _Ty>
_NODISCARD constexpr bool _Is_finite(const _Ty& _Xx) noexcept {
    return _Xx < 1e38;
}

template <class _Ty>
_NODISCARD constexpr auto _Float_abs_bits(const _Ty& _Xx) noexcept {
    return _Xx >= 0 ? _Xx : -_Xx;
}

}

int main()
{
    return 0;
}
