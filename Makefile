.PHONY: build run clean test

BIN_NAME=ggpktool
CMD_PATH=./cmd/ggpktool

build:
	@echo "Building $(BIN_NAME)..."
	@go build -o $(BIN_NAME) $(CMD_PATH)/main.go

run: build
	@echo "Running $(BIN_NAME)..."
	@./$(BIN_NAME)

clean:
	@echo "Cleaning..."
	@go clean
	@rm -f $(BIN_NAME)

test:
	@echo "Running tests..."
	@go test ./...

# Placeholder for future commands
install-deps:
	@echo "Installing dependencies (if any)..."
	@go mod tidy

.DEFAULT_GOAL := build
