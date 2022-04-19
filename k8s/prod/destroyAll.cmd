@echo off
REM destroys all K8s services in "akkastress" namespace
REM we don't destroy the namespace itself since it contains the Azure Storage secrets we need for Akka.Persistence.Azure

set namespace=akkastress
set location=%~dp0/environment

echo "Destroying K8s resources from [%location%] in namespace [%namespace%]"

echo "Using namespace [%namespace%] going forward..."

echo "Creating configurations from YAML files in [%location%/configs]"
for %%f in (%location%/configs/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl delete -f "%location%/configs/%%~nxf" -n "%namespace%"
)

echo "Creating environment-specific services from YAML files in [%location%]"
for %%f in (%location%/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl delete -f "%location%/%%~nxf" -n "%namespace%"
)

echo "Creating all services..."
for %%f in (%~dp0/services/*.yaml) do (
    echo "Deploying %%~nxf"
    kubectl delete -f "%~dp0/services/%%~nxf" -n "%namespace%"
)

echo "All services started... Printing K8s output.."
kubectl get all -n "%namespace%"