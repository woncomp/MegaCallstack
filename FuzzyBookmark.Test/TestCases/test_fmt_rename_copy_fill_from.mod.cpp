// Expect: 11
#include <cstring>

namespace fmt {

struct basic_specs {
    char fill[4] = { ' ', 0, 0, 0 };

    void copy_fill_from(const basic_specs& specs) {
        for (size_t i = 0; i < sizeof(fill); ++i) {
            fill[i] = specs.fill[i];
        }
    }
};

void apply_specs(basic_specs& dst, const basic_specs& src) {
    dst.copy_fill_from(src);
}

}

int main()
{
    return 0;
}
