docker build . -f docker/docker-dev -t kyle/redis-benchmark

docker-compose -f docker\docker-compose-dev.yml up --exit-code-from benchmark

Command Line Options:

--app_threads           (Default: 48) Set the number of application threads.

-d, --dynamic           (Default: true) Application threads are created with 1 second interval.

--service_stack         (Default: false) Uses the ServiceStack.Redis library instead of the StackExchange.Redis library.

--async                 (Default: false) Uses the StackExchange.Redis asynchronous interface (but still with a blocked wait for the result).

--pooled_connections    (Default: 1) Set the number of pooled connections (ServiceStack only)

--worker_threads        (Default: 64) Set the number of .net worker threads.

--io_threads            (Default: 64) Set the number of .net io threads.

--hash_size             (Default: 15) Set the number of fields in a hash.

--payload_size          (Default: 128) Set the size of each field in a hash.

-k, --key_name          (Default: key) Set the hash key name.

-m, --mux_count         (Default: 1) Set the number of multiplexers.

-t, --time_out          (Default: 1) Set the timeout (in minutes).

-r, --requests          (Default: 10000) Number of requests to execute for each application thread