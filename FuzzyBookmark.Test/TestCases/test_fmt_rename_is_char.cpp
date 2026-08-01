// Bookmark: 7
#include <string_view>

template <typename Char>
struct is_char {
    static constexpr bool value = false;
};

template <>
struct is_char<char> {
    static constexpr bool value = true;
};

template <typename Char, typename Enable = void>
struct string_view_converter;

template <typename Char>
struct string_view_converter<Char, typename std::enable_if<is_char<Char>::value>::type> {
    static std::basic_string_view<Char> convert(const Char* s) {
        return std::basic_string_view<Char>(s);
    }
};

int main()
{
    return 0;
}
