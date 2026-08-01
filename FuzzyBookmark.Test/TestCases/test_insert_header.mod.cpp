// Expect: 10
// This header was inserted above the original code.
// It changes all absolute line numbers but leaves the
// structural context of the target line intact.
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        void Run()
        {
            int value = 42;
            std::cout << value << std::endl;
        }
    };
}
