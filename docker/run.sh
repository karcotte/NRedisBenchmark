#!/usr/bin/env bash
sleep 5
curl -k --header "Content-Type: application/json" --request POST --data '{ "cluster": { "nodes": [], "name": "test.cluster" }, "credentials": { "username": "admin@redislabs.com", "password": "admin" } }' https://redis:9443/v1/bootstrap/create_cluster
sleep 5
curl -k -u admin@redislabs.com:admin --header "Content-Type: application/json" --request POST --data '{ "name": "test-db", "type": "redis", "memory_size": 1073741824, "port": 12000 }' https://redis:9443/v1/bdbs
/wait-for-it.sh -t 0 redis:12000 -- dotnet NRedisBenchmark.dll --app_threads=24