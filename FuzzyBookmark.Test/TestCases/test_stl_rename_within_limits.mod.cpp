// Expect: 18
#include <iterator>
#include <limits>

namespace std {

template <class _InIt, class _Ty>
inline constexpr bool _Vector_alg_in_find_is_safe = false;

template <class _InIt, class _Ty>
bool _Could_compare_equal_to_value_type(const _Ty& _Val) {
    return _Val >= 0;
}

template <class _InIt, class _Ty>
bool _Find_impl(_InIt _First, _InIt _Last, const _Ty& _Val) {
    for (; _First != _Last; ++_First) {
        if (*_First == _Val) {
            return true;
        }
    }
    return false;
}

}

int main()
{
    return 0;
}
