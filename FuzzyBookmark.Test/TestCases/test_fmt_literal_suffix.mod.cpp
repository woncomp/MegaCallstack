// Expect: 12
#include <fmt/format.h>

template <typename T = void>
struct basic_data {
    static constexpr uint32_t fractional_part_rounding_thresholds[8] = {
        2576980378U,
        2190433321U,
        2151778616U,
        2147913145U,
        2147526598U,
        2147487943U,
        2147484078U,
        2147483691U
    };
};

int main()
{
    return 0;
}
