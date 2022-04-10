@echo off
REM destroys all K8s services in "phobos-web" namespace

kubectl delete ns phobos-web