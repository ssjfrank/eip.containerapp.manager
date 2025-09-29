#!/bin/bash

# ContainerManager.Service - Docker Test Runner
# Automated testing script for Docker environment

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
IMAGE_NAME="container-manager:test"
CONTAINER_NAME="cm-test"
CONFIG_FILE="appsettings.test.json"
LOG_DIR="logs"

# Print colored output
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_header() {
    echo ""
    echo -e "${BLUE}================================================${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}================================================${NC}"
    echo ""
}

# Check prerequisites
check_prerequisites() {
    print_header "Checking Prerequisites"

    # Check Docker is running
    if ! docker info > /dev/null 2>&1; then
        print_error "Docker is not running. Please start Docker Desktop."
        exit 1
    fi
    print_success "Docker is running"

    # Check configuration file exists
    if [ ! -f "$CONFIG_FILE" ]; then
        print_error "Configuration file $CONFIG_FILE not found."
        print_info "Please create $CONFIG_FILE from appsettings.json template"
        exit 1
    fi
    print_success "Configuration file found"

    # Create logs directory if not exists
    mkdir -p "$LOG_DIR"
    print_success "Logs directory ready"
}

# Build Docker image
build_image() {
    print_header "Building Docker Image"

    print_info "Building image: $IMAGE_NAME"
    if docker build -t "$IMAGE_NAME" . ; then
        print_success "Image built successfully"
        docker images "$IMAGE_NAME"
    else
        print_error "Image build failed"
        exit 1
    fi
}

# Clean up existing container
cleanup_container() {
    if docker ps -a | grep -q "$CONTAINER_NAME"; then
        print_info "Removing existing container: $CONTAINER_NAME"
        docker rm -f "$CONTAINER_NAME" > /dev/null 2>&1 || true
    fi
}

# Test 1: Basic startup test
test_startup() {
    print_header "Test 1: Basic Startup & Connection"

    cleanup_container

    print_info "Starting container with test configuration..."
    docker run -d \
        --name "$CONTAINER_NAME" \
        -v "$(pwd)/$CONFIG_FILE:/app/appsettings.json" \
        -v "$(pwd)/$LOG_DIR:/app/logs" \
        "$IMAGE_NAME"

    print_info "Waiting 10 seconds for initialization..."
    sleep 10

    print_info "Checking container status..."
    if docker ps | grep -q "$CONTAINER_NAME"; then
        print_success "Container is running"
    else
        print_error "Container stopped unexpectedly"
        docker logs "$CONTAINER_NAME"
        exit 1
    fi

    print_info "Checking logs for successful initialization..."
    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "All services initialized successfully"; then
        print_success "Services initialized successfully"
    else
        print_warning "Services may not be fully initialized"
    fi

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "ContainerManager MonitoringWorker starting"; then
        print_success "MonitoringWorker started"
    else
        print_error "MonitoringWorker failed to start"
        docker logs "$CONTAINER_NAME"
        exit 1
    fi

    print_info "Recent logs:"
    docker logs --tail 20 "$CONTAINER_NAME"
}

# Test 2: Check EMS connection
test_ems_connection() {
    print_header "Test 2: EMS Connection"

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "EMS connected"; then
        print_success "EMS connection successful"
    elif docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Failed to initialize EMS"; then
        print_error "EMS connection failed"
        print_info "Check your EMS configuration in $CONFIG_FILE"
        docker logs "$CONTAINER_NAME" | grep -i "ems"
        exit 1
    else
        print_warning "EMS connection status unclear"
    fi
}

# Test 3: Check Azure connection
test_azure_connection() {
    print_header "Test 3: Azure Connection"

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Azure Container Apps client initialized"; then
        print_success "Azure client initialized"
    elif docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Failed to initialize Azure"; then
        print_error "Azure connection failed"
        print_info "Check your Azure configuration in $CONFIG_FILE"
        docker logs "$CONTAINER_NAME" | grep -i "azure"
        exit 1
    else
        print_warning "Azure connection status unclear"
    fi
}

# Test 4: Monitor for errors
test_error_monitoring() {
    print_header "Test 4: Error Monitoring (30 seconds)"

    print_info "Monitoring for errors in logs..."
    sleep 30

    ERROR_COUNT=$(docker logs "$CONTAINER_NAME" 2>&1 | grep -c "\[ERR\]" || true)
    CRITICAL_COUNT=$(docker logs "$CONTAINER_NAME" 2>&1 | grep -c "\[CRT\]" || true)

    if [ "$ERROR_COUNT" -gt 0 ]; then
        print_warning "Found $ERROR_COUNT error(s) in logs"
        docker logs "$CONTAINER_NAME" 2>&1 | grep "\[ERR\]" | tail -5
    else
        print_success "No errors found"
    fi

    if [ "$CRITICAL_COUNT" -gt 0 ]; then
        print_error "Found $CRITICAL_COUNT critical error(s)"
        docker logs "$CONTAINER_NAME" 2>&1 | grep "\[CRT\]"
        exit 1
    fi
}

# Test 5: Check monitoring loop
test_monitoring_loop() {
    print_header "Test 5: Monitoring Loop Activity"

    print_info "Checking for monitoring activity..."

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Retrieving queue information"; then
        print_success "Monitoring loop is active"
    else
        print_warning "No monitoring activity detected yet"
    fi

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Retrieved .* queues"; then
        QUEUE_COUNT=$(docker logs "$CONTAINER_NAME" 2>&1 | grep "Retrieved .* queues" | tail -1)
        print_success "$QUEUE_COUNT"
    fi
}

# Test 6: Graceful shutdown
test_graceful_shutdown() {
    print_header "Test 6: Graceful Shutdown"

    print_info "Sending stop signal to container..."
    docker stop -t 40 "$CONTAINER_NAME"

    print_info "Checking shutdown logs..."
    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Waiting for .* background operations"; then
        print_success "Background operations cleanup initiated"
    fi

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "ContainerManager MonitoringWorker stopping"; then
        print_success "Worker stopped gracefully"
    fi

    if docker logs "$CONTAINER_NAME" 2>&1 | grep -q "Disposing ContainerManager resources"; then
        print_success "Resources disposed"
    fi

    print_success "Graceful shutdown completed"
}

# View logs
view_logs() {
    print_header "Complete Log Output"

    print_info "Container logs:"
    docker logs "$CONTAINER_NAME"

    print_info ""
    print_info "Log files in $LOG_DIR:"
    ls -lh "$LOG_DIR"
}

# Interactive mode
interactive_mode() {
    print_header "Interactive Testing Mode"

    cleanup_container

    print_info "Starting container in interactive mode..."
    print_info "Press Ctrl+C to stop"
    print_info ""

    docker run --rm \
        --name "$CONTAINER_NAME" \
        -v "$(pwd)/$CONFIG_FILE:/app/appsettings.json" \
        -v "$(pwd)/$LOG_DIR:/app/logs" \
        "$IMAGE_NAME"
}

# Clean up
cleanup() {
    print_header "Cleanup"

    cleanup_container
    print_success "Cleanup complete"
}

# Show usage
usage() {
    echo "ContainerManager.Service - Docker Test Runner"
    echo ""
    echo "Usage: $0 [OPTION]"
    echo ""
    echo "Options:"
    echo "  all           Run all automated tests (default)"
    echo "  build         Build Docker image only"
    echo "  startup       Test 1: Basic startup"
    echo "  interactive   Run in interactive mode (live logs)"
    echo "  logs          View complete logs from last run"
    echo "  cleanup       Remove test container"
    echo "  help          Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0            # Run all tests"
    echo "  $0 build      # Just build the image"
    echo "  $0 interactive # Run with live logs"
    echo ""
}

# Main script
main() {
    local test_mode="${1:-all}"

    case "$test_mode" in
        all)
            check_prerequisites
            build_image
            test_startup
            test_ems_connection
            test_azure_connection
            test_error_monitoring
            test_monitoring_loop
            test_graceful_shutdown
            view_logs
            cleanup
            print_success "All tests completed!"
            ;;
        build)
            check_prerequisites
            build_image
            ;;
        startup)
            check_prerequisites
            build_image
            test_startup
            ;;
        interactive)
            check_prerequisites
            build_image
            interactive_mode
            ;;
        logs)
            if docker ps -a | grep -q "$CONTAINER_NAME"; then
                view_logs
            else
                print_error "No container found. Run tests first."
                exit 1
            fi
            ;;
        cleanup)
            cleanup
            ;;
        help|--help|-h)
            usage
            ;;
        *)
            print_error "Unknown option: $test_mode"
            usage
            exit 1
            ;;
    esac
}

# Run main function
main "$@"