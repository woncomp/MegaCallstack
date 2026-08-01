// Expect: 14
#include <iostream>

#define TRACE_START() do { } while(0)
#define TRACE_END() do { } while(0)
#define LOG_VALUE(v) do { std::cout << (v) << std::endl; } while(0)

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
