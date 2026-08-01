// Bookmark: 9
#include <limits>

namespace std {

template <class _Int>
bool _Add_with_overflow_check(const _Int _Left, const _Int _Right, _Int& _Out) {
    _Out = _Left + _Right;
    return _Out >= _Left;
}

template <class _Int>
bool _Multiply_with_overflow_check(const _Int _Left, const _Int _Right, _Int& _Out) {
    _Out = _Left * _Right;
    return _Out >= _Left;
}

}

int main()
{
    return 0;
}
