// Expect: 12
#include <iostream>

namespace App
{
    class Processor
    {
    public:
        void Run()
        {
            int alpha = 1;
            int value = 100;
            std::cout << value << std::endl;
        }
    };
}
