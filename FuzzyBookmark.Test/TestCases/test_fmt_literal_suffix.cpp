// Bookmark: 12
#include <fmt/format.h>

template <typename T = void>
struct basic_data {
    static constexpr uint32_t fractional_part_rounding_thresholds[8] = {
        2576980378,
        2190433321,
        2151778616,
        2147913145,
        2147526598,
        2147487943,
        2147484078,
        2147483691
    };
};

int main()
{
    return 0;
}
