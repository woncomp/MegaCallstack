// Expect: 10
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        void NewName()
        {
            int value = 42;
            std::cout << value << std::endl;
        }
    };
}
