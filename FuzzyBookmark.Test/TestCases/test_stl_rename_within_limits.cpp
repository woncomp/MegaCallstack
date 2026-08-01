// Bookmark: 15
#include <iterator>
#include <limits>

namespace std {

template <class _InIt, class _Ty>
bool _Within_limits(const _InIt&, const _Ty& _Val) {
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
