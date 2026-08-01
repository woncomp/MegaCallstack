// Expect: 20
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        void Handle(double d)
        {
            int value = 7;
            std::cout << d << std::endl;
        }

        void Handle(int x)
        {
            int value = 42;
            std::cout << value + x << std::endl;
        }

        void Handle(const char* msg)
        {
            int value = 99;
            std::cout << msg << value << std::endl;
        }
    };
}
